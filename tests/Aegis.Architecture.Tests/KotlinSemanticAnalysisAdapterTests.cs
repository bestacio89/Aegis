using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinSemanticAnalysisAdapterTests
{
    [Fact]
    public async Task Registers_only_completed_compiler_outputs_for_the_correct_scope()
    {
        var worker = new RecordingKotlinWorkerClient();
        var adapter = new KotlinSemanticAnalysisAdapter(
            worker,
            NullLogger<KotlinSemanticAnalysisAdapter>.Instance);
        await using var executionContext = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);
        var request = CreateRequest(includeTestCode: true);

        var run = await adapter.AnalyzeAsync(request, executionContext);

        Assert.Equal(SemanticAnalysisStatus.Completed, run.Status);
        var output = Assert.Single(executionContext.GetCompilationOutputs(request.ProjectPath, includeTestCode: true));
        Assert.EndsWith(Path.Combine("classes"), output, StringComparison.Ordinal);
        Assert.Empty(executionContext.GetCompilationOutputs(request.ProjectPath, includeTestCode: false));
        Assert.Equal(1, worker.Calls);
    }

    [Fact]
    public async Task Marks_inferred_compiler_configuration_partial_and_does_not_publish_output()
    {
        var adapter = new KotlinSemanticAnalysisAdapter(
            new RecordingKotlinWorkerClient(),
            NullLogger<KotlinSemanticAnalysisAdapter>.Instance);
        await using var executionContext = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);
        var request = CreateRequest();
        var options = request.ProviderOptions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        {
            ["kotlin.configurationInferred"] = "true"
        };
        request = CopyWithOptions(request, options);

        var run = await adapter.AnalyzeAsync(request, executionContext);

        Assert.Equal(SemanticAnalysisStatus.PartiallyCompleted, run.Status);
        Assert.Contains(run.Diagnostics, diagnostic =>
            diagnostic.Code == "KotlinCompilerConfigurationInferred"
            && diagnostic.AffectsCoverage);
        Assert.Empty(executionContext.GetCompilationOutputs(request.ProjectPath, includeTestCode: true));
    }

    [Fact]
    public async Task Marks_incomplete_external_classpath_partial_and_does_not_publish_output()
    {
        var adapter = new KotlinSemanticAnalysisAdapter(
            new RecordingKotlinWorkerClient(),
            NullLogger<KotlinSemanticAnalysisAdapter>.Instance);
        await using var executionContext = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);
        var request = CopyWithOptions(CreateRequest(), new Dictionary<string, string>
        {
            ["kotlin.compilerVersion"] = "2.4.21",
            ["kotlin.languageVersion"] = "2.4",
            ["kotlin.apiVersion"] = "2.4",
            ["kotlin.jvmTarget"] = "21",
            ["kotlin.configurationInferred"] = "false"
        }, hasDeclaredDependencies: true, classpathComplete: false);

        var run = await adapter.AnalyzeAsync(request, executionContext);

        Assert.Equal(SemanticAnalysisStatus.PartiallyCompleted, run.Status);
        Assert.Contains(run.Diagnostics, diagnostic =>
            diagnostic.Code == "KotlinExternalClasspathUnavailable"
            && diagnostic.AffectsCoverage);
        Assert.Empty(executionContext.GetCompilationOutputs(request.ProjectPath, includeTestCode: true));
    }

    [Fact]
    public async Task Marks_unexecuted_compiler_plugins_partial()
    {
        var adapter = new KotlinSemanticAnalysisAdapter(
            new RecordingKotlinWorkerClient(),
            NullLogger<KotlinSemanticAnalysisAdapter>.Instance);
        await using var executionContext = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);
        var request = CreateRequest();
        var options = request.ProviderOptions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        {
            ["kotlin.compilerPluginsNotExecuted"] = "true"
        };
        request = CopyWithOptions(request, options);

        var run = await adapter.AnalyzeAsync(request, executionContext);

        Assert.Equal(SemanticAnalysisStatus.PartiallyCompleted, run.Status);
        Assert.Contains(run.Diagnostics, diagnostic =>
            diagnostic.Code == "KotlinCompilerPluginsNotExecuted"
            && diagnostic.AffectsCoverage);
        Assert.Empty(executionContext.GetCompilationOutputs(request.ProjectPath, includeTestCode: true));
    }

    [Fact]
    public async Task Reports_worker_as_unsupported_when_not_deployed()
    {
        var worker = new RecordingKotlinWorkerClient(isConfigured: false);
        var adapter = new KotlinSemanticAnalysisAdapter(
            worker,
            NullLogger<KotlinSemanticAnalysisAdapter>.Instance);
        await using var executionContext = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);

        var run = await adapter.AnalyzeAsync(CreateRequest(), executionContext);

        Assert.Equal(SemanticAnalysisStatus.Unsupported, run.Status);
        Assert.Contains(run.Diagnostics, diagnostic => diagnostic.Code == "KotlinWorkerNotConfigured");
        Assert.Equal(0, worker.Calls);
    }

    private static SemanticAnalysisRequest CreateRequest(bool includeTestCode = false) =>
        new()
        {
            Language = "Kotlin",
            ProjectPath = Path.Combine(Path.GetTempPath(), "kotlin-service"),
            WorkspaceRoot = Path.GetTempPath(),
            SourceFiles = ["Service.kt"],
            SourceRoots = ["src/main/kotlin"],
            ProviderOptions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kotlin.compilerVersion"] = "2.4.21",
                ["kotlin.languageVersion"] = "2.4",
                ["kotlin.apiVersion"] = "2.4",
                ["kotlin.jvmTarget"] = "21",
                ["kotlin.moduleName"] = "kotlin_service",
                ["kotlin.configurationInferred"] = "false"
            },
            IncludeTestCode = includeTestCode
        };

    private static SemanticAnalysisRequest CopyWithOptions(
        SemanticAnalysisRequest request,
        IReadOnlyDictionary<string, string> options,
        bool? hasDeclaredDependencies = null,
        bool? classpathComplete = null) =>
        new()
        {
            Language = request.Language,
            ProjectPath = request.ProjectPath,
            WorkspaceRoot = request.WorkspaceRoot,
            ModuleName = request.ModuleName,
            BuildSystem = request.BuildSystem,
            SourceFiles = request.SourceFiles,
            SourceRoots = request.SourceRoots,
            ReferencedSourceRoots = request.ReferencedSourceRoots,
            TestSourceRoots = request.TestSourceRoots,
            GeneratedSourceRoots = request.GeneratedSourceRoots,
            ClasspathEntries = request.ClasspathEntries,
            UnresolvedClasspathDependencies = hasDeclaredDependencies == true
                ? ["org.example:missing:1.0"]
                : request.UnresolvedClasspathDependencies,
            ClasspathResolutionComplete = classpathComplete ?? request.ClasspathResolutionComplete,
            BuildFiles = request.BuildFiles,
            RequestedCapabilities = request.RequestedCapabilities,
            ProviderOptions = options,
            SourceLevel = request.SourceLevel,
            HasDeclaredDependencies = hasDeclaredDependencies ?? request.HasDeclaredDependencies,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
            IncludeTestCode = request.IncludeTestCode
        };

    private sealed class RecordingKotlinWorkerClient(bool isConfigured = true) : IKotlinWorkerClient
    {
        public int Calls { get; private set; }

        public bool IsConfigured => isConfigured;

        public ValueTask<KotlinWorkerAnalysisResult> AnalyzeAsync(
            SemanticAnalysisRequest request,
            KotlinCompilerConfiguration configuration,
            string artifactRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var outputDirectory = Path.Combine(artifactRoot, "classes");
            Directory.CreateDirectory(outputDirectory);
            var run = new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = "Kotlin",
                EngineId = "kotlin-k2-ir",
                EngineVersion = "2.4.21",
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = request.SourceFiles.Count,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
            return ValueTask.FromResult(new KotlinWorkerAnalysisResult(run, outputDirectory, true));
        }
    }
}
