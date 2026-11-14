using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinJavaInteropPipelineTests
{
    [Fact]
    public async Task Kotlin_outputs_flow_to_java_in_matching_production_and_test_scopes()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var modulePath = Path.Combine(repository, "service");
        var kotlinMainRoot = Path.Combine(modulePath, "src", "main", "kotlin");
        var kotlinTestRoot = Path.Combine(modulePath, "src", "test", "kotlin");
        var javaMainRoot = Path.Combine(modulePath, "src", "main", "java");
        var javaTestRoot = Path.Combine(modulePath, "src", "test", "java");
        Directory.CreateDirectory(kotlinMainRoot);
        Directory.CreateDirectory(kotlinTestRoot);
        Directory.CreateDirectory(javaMainRoot);
        Directory.CreateDirectory(javaTestRoot);

        await File.WriteAllTextAsync(
            Path.Combine(kotlinMainRoot, "KotlinService.kt"),
            "class KotlinService");
        await File.WriteAllTextAsync(
            Path.Combine(kotlinTestRoot, "KotlinServiceTests.kt"),
            "class KotlinServiceTests");
        await File.WriteAllTextAsync(
            Path.Combine(javaMainRoot, "JavaService.java"),
            "class JavaService {}");
        await File.WriteAllTextAsync(
            Path.Combine(javaTestRoot, "JavaServiceTests.java"),
            "class JavaServiceTests {}");

        try
        {
            var module = new ProjectModuleContext
            {
                Name = "service",
                Path = modulePath,
                BuildSystem = ArchitectureBuildSystem.Gradle,
                Languages = ["Kotlin", "Java"],
                GradleBuildModel = new GradleBuildModel
                {
                    Plugins =
                    [
                        new GradleBuildModel.Plugin("org.jetbrains.kotlin.jvm", "2.4.21", true)
                    ],
                    KotlinPluginVersion = "2.4.21",
                    KotlinLanguageVersion = "2.4",
                    KotlinApiVersion = "2.4",
                    KotlinJvmTarget = "21"
                },
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = kotlinMainRoot,
                        Ownership = ProjectSourceRootOwnership.Production,
                        Origin = ProjectSourceRootOrigin.Conventional,
                        Languages = ["Kotlin"]
                    },
                    new ProjectSourceRoot
                    {
                        Directory = kotlinTestRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Conventional,
                        Languages = ["Kotlin"]
                    },
                    new ProjectSourceRoot
                    {
                        Directory = javaMainRoot,
                        Ownership = ProjectSourceRootOwnership.Production,
                        Origin = ProjectSourceRootOrigin.Conventional,
                        ContainsJavaSources = true,
                        Languages = ["Java"]
                    },
                    new ProjectSourceRoot
                    {
                        Directory = javaTestRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Conventional,
                        ContainsJavaSources = true,
                        Languages = ["Java"]
                    }
                ]
            };

            var context = new ProjectArchitectureContext
            {
                ProjectName = "service",
                RootPath = repository,
                Language = "Kotlin",
                Modules = [module]
            };
            var executionOrder = new List<string>();
            var kotlinAdapter = new RecordingKotlinExecutionAdapter(executionOrder);
            var javaAdapter = new RecordingJavaAdapter(executionOrder);
            var classpathResolver = new EmptyClasspathResolver();
            var kotlinOrchestrator = new KotlinSemanticAnalysisOrchestrator(
                [kotlinAdapter],
                classpathResolver,
                NullLogger<KotlinSemanticAnalysisOrchestrator>.Instance);
            var javaOrchestrator = new JavaSemanticAnalysisOrchestrator(
                [javaAdapter],
                classpathResolver,
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);
            var pipeline = new SemanticAnalysisPipeline(
                [javaOrchestrator, kotlinOrchestrator],
                NullLogger<SemanticAnalysisPipeline>.Instance);

            await pipeline.AnalyzeAsync(context);

            Assert.Equal(["Kotlin", "Kotlin", "Java", "Java"], executionOrder);
            var javaProduction = Assert.Single(javaAdapter.Requests.Where(request => !request.IncludeTestCode));
            var javaTest = Assert.Single(javaAdapter.Requests.Where(request => request.IncludeTestCode));
            Assert.Equal(2, kotlinAdapter.OutputDirectories.Count);
            var kotlinProductionOutput = kotlinAdapter.OutputDirectories[0];
            var kotlinTestOutput = kotlinAdapter.OutputDirectories[1];

            Assert.Contains(kotlinProductionOutput, javaProduction.ClasspathEntries);
            Assert.DoesNotContain(kotlinTestOutput, javaProduction.ClasspathEntries);
            Assert.Contains(kotlinProductionOutput, javaTest.ClasspathEntries);
            Assert.Contains(kotlinTestOutput, javaTest.ClasspathEntries);
            Assert.All(kotlinAdapter.OutputDirectories, output => Assert.False(Directory.Exists(output)));
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private sealed class RecordingKotlinExecutionAdapter(ICollection<string> executionOrder)
        : ISemanticAnalysisExecutionAdapter
    {
        public List<string> OutputDirectories { get; } = [];
        public string AdapterId => "fixture-kotlin";
        public string Language => "Kotlin";
        public IReadOnlyCollection<string> SupportedCapabilities =>
            ["symbols", "relationships", "compiler-diagnostics", "jvm-interop"];

        public bool CanAnalyze(SemanticAnalysisRequest request) =>
            request.Language.Equals(Language, StringComparison.OrdinalIgnoreCase);

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateRun(request));

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            SemanticAnalysisExecutionContext executionContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            executionOrder.Add("Kotlin");
            var artifactRoot = executionContext.CreateArtifactRoot();
            var outputDirectory = Path.Combine(artifactRoot, "classes");
            Directory.CreateDirectory(outputDirectory);
            executionContext.RegisterCompilationOutput(
                request.ProjectPath,
                request.IncludeTestCode,
                artifactRoot,
                outputDirectory);
            OutputDirectories.Add(outputDirectory);
            return ValueTask.FromResult(CreateRun(request));
        }

        private SemanticAnalysisRun CreateRun(SemanticAnalysisRequest request) =>
            new()
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = Language,
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = request.SourceFiles.Count
            };
    }

    private sealed class RecordingJavaAdapter(ICollection<string> executionOrder)
        : ISemanticAnalysisAdapter
    {
        public List<SemanticAnalysisRequest> Requests { get; } = [];
        public string AdapterId => "fixture-java";
        public string Language => "Java";
        public IReadOnlyCollection<string> SupportedCapabilities => ["symbols", "relationships"];

        public bool CanAnalyze(SemanticAnalysisRequest request) =>
            request.Language.Equals(Language, StringComparison.OrdinalIgnoreCase);

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            executionOrder.Add("Java");
            Requests.Add(request);
            return ValueTask.FromResult(new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = Language,
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = request.SourceFiles.Count
            });
        }
    }

    private sealed class EmptyClasspathResolver : IJvmClasspathResolver
    {
        public JvmClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode) =>
            new(Array.Empty<string>(), Array.Empty<string>(), false, true);
    }
}
