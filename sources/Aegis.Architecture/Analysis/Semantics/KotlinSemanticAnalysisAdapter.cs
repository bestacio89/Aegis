using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>Maps pinned Kotlin K2 worker evidence into Aegis semantic contracts.</summary>
public sealed class KotlinSemanticAnalysisAdapter : ISemanticAnalysisExecutionAdapter
{
    private const string DefaultCompilerVersion = "2.4.21";
    private const string DefaultLanguageVersion = "2.4";
    private const string DefaultApiVersion = "2.4";
    private const string DefaultJvmTarget = "1.8";
    private static readonly IReadOnlyCollection<string> Capabilities =
        Array.AsReadOnly(new[] { "symbols", "relationships", "compiler-diagnostics", "jvm-interop" });

    private readonly IKotlinWorkerClient _workerClient;
    private readonly ILogger<KotlinSemanticAnalysisAdapter> _logger;

    public KotlinSemanticAnalysisAdapter(
        IKotlinWorkerClient workerClient,
        ILogger<KotlinSemanticAnalysisAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(workerClient);
        ArgumentNullException.ThrowIfNull(logger);
        _workerClient = workerClient;
        _logger = logger;
    }

    public string AdapterId => "kotlin-k2-ir";

    public string Language => "Kotlin";

    public IReadOnlyCollection<string> SupportedCapabilities => Capabilities;

    public bool CanAnalyze(SemanticAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Language.Equals(Language, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(request.ProjectPath)
            && request.SourceFiles.Count > 0
            && request.SourceRoots.Count > 0;
    }

    public async ValueTask<SemanticAnalysisRun> AnalyzeAsync(
        SemanticAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var executionContext = new SemanticAnalysisExecutionContext(_logger);
        return await AnalyzeAsync(request, executionContext, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SemanticAnalysisRun> AnalyzeAsync(
        SemanticAnalysisRequest request,
        SemanticAnalysisExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executionContext);
        if (!CanAnalyze(request))
            throw new ArgumentException("The request does not describe an analyzable Kotlin module.", nameof(request));

        if (!_workerClient.IsConfigured)
        {
            return CreateUnavailableRun(
                request,
                SemanticAnalysisStatus.Unsupported,
                "KotlinWorkerNotConfigured",
                "The Kotlin K2 worker is not deployed. Configure its private worker JAR, pinned libraries and Java 21 runtime.");
        }

        var (configuration, configurationInferred) = ResolveConfiguration(request);
        var artifactRoot = executionContext.CreateArtifactRoot();

        KotlinWorkerAnalysisResult result;
        try
        {
            result = await _workerClient.AnalyzeAsync(
                request,
                configuration,
                artifactRoot,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Kotlin semantic analysis failed for module {ProjectPath}.",
                request.ProjectPath);
            return CreateUnavailableRun(
                request,
                SemanticAnalysisStatus.Failed,
                "KotlinSemanticAnalysisFailed",
                "The Kotlin worker failed while analysing the module.",
                exception.GetType().Name);
        }

        var run = result.Run;
        var diagnostics = run.Diagnostics.ToList();
        var status = run.Status;

        if (configurationInferred)
        {
            status = status == SemanticAnalysisStatus.Completed
                ? SemanticAnalysisStatus.PartiallyCompleted
                : status;
            diagnostics.Add(new AnalysisDiagnostic
            {
                Code = "KotlinCompilerConfigurationInferred",
                Component = nameof(KotlinSemanticAnalysisAdapter),
                Message = "One or more Kotlin compiler settings were not declared by the build model. The pinned worker defaults were used; language/API compatibility and JVM target equivalence are not guaranteed.",
                Target = request.ProjectPath,
                AffectsCoverage = true
            });
        }

        if (request.HasDeclaredDependencies && !request.ClasspathResolutionComplete)
        {
            status = status == SemanticAnalysisStatus.Completed
                ? SemanticAnalysisStatus.PartiallyCompleted
                : status;
            diagnostics.Add(new AnalysisDiagnostic
            {
                Code = "KotlinExternalClasspathUnavailable",
                Component = nameof(KotlinSemanticAnalysisAdapter),
                Message = CreateClasspathDiagnostic(request),
                Target = request.ProjectPath,
                AffectsCoverage = true
            });
        }

        if (bool.TryParse(
                ReadOption(request.ProviderOptions, "kotlin.compilerPluginsNotExecuted"),
                out var compilerPluginsNotExecuted)
            && compilerPluginsNotExecuted)
        {
            status = status == SemanticAnalysisStatus.Completed
                ? SemanticAnalysisStatus.PartiallyCompleted
                : status;
            diagnostics.Add(new AnalysisDiagnostic
            {
                Code = "KotlinCompilerPluginsNotExecuted",
                Component = nameof(KotlinSemanticAnalysisAdapter),
                Message = "The build declares Kotlin compiler plugins or code generators that Aegis does not execute. Generated or transformed declarations may be missing from semantic evidence.",
                Target = request.ProjectPath,
                AffectsCoverage = true
            });
        }

        if (status == SemanticAnalysisStatus.Completed
            && !result.CompilationSucceeded)
        {
            status = SemanticAnalysisStatus.Failed;
            diagnostics.Add(new AnalysisDiagnostic
            {
                Code = "KotlinCompilationOutputNotProduced",
                Component = nameof(KotlinSemanticAnalysisAdapter),
                Message = "The Kotlin worker reported a completed analysis without a successful compiler result. Its semantic output cannot be treated as complete.",
                Target = request.ProjectPath,
                AffectsCoverage = true
            });
        }

        var compilerPluginsWereSkipped = bool.TryParse(
                ReadOption(request.ProviderOptions, "kotlin.compilerPluginsNotExecuted"),
                out var skippedCompilerPlugins)
            && skippedCompilerPlugins;
        var canPublishBindingOutput = result.CompilationSucceeded
            && run.Status is SemanticAnalysisStatus.Completed or SemanticAnalysisStatus.PartiallyCompleted
            && !configurationInferred
            && !compilerPluginsWereSkipped;

        if (canPublishBindingOutput)
        {
            if (string.IsNullOrWhiteSpace(result.CompilationOutputDirectory))
            {
                status = SemanticAnalysisStatus.PartiallyCompleted;
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "KotlinCompilationOutputUnavailable",
                    Component = nameof(KotlinSemanticAnalysisAdapter),
                    Message = "The Kotlin compiler completed without returning a usable JVM output directory; downstream Java binding was not enabled.",
                    Target = request.ProjectPath,
                    AffectsCoverage = true
                });
            }
            else
            {
                try
                {
                    executionContext.RegisterCompilationOutput(
                        request.ProjectPath,
                        request.IncludeTestCode,
                        artifactRoot,
                        result.CompilationOutputDirectory);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                    or ArgumentException
                    or IOException
                    or UnauthorizedAccessException)
                {
                    status = SemanticAnalysisStatus.Failed;
                    diagnostics.Add(new AnalysisDiagnostic
                    {
                        Code = "KotlinCompilationArtifactRejected",
                        Component = nameof(KotlinSemanticAnalysisAdapter),
                        Message = "The Kotlin compiler output failed host-side artifact ownership validation and was not exposed to downstream analyzers.",
                        Target = request.ProjectPath,
                        ExceptionType = exception.GetType().Name,
                        AffectsCoverage = true
                    });
                    _logger.LogError(
                        exception,
                        "Rejected Kotlin compiler output for module {ProjectPath}.",
                        request.ProjectPath);
                }
            }
        }

        return new SemanticAnalysisRun
        {
            RunId = run.RunId,
            Language = run.Language,
            EngineId = run.EngineId,
            EngineVersion = run.EngineVersion,
            ProjectPath = run.ProjectPath,
            Status = status,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            AnalyzedFileCount = run.AnalyzedFileCount,
            SkippedFileCount = run.SkippedFileCount,
            Capabilities = run.Capabilities,
            Symbols = run.Symbols,
            Relationships = run.Relationships,
            Diagnostics = diagnostics
        };
    }

    private static (KotlinCompilerConfiguration Configuration, bool IsInferred) ResolveConfiguration(
        SemanticAnalysisRequest request)
    {
        var options = request.ProviderOptions;
        var compilerVersion = ReadOption(options, "kotlin.compilerVersion");
        var languageVersion = ReadOption(options, "kotlin.languageVersion");
        var apiVersion = ReadOption(options, "kotlin.apiVersion");
        var jvmTarget = ReadOption(options, "kotlin.jvmTarget");
        var moduleName = ReadOption(options, "kotlin.moduleName")
            ?? $"aegis_{Path.GetFileName(request.ProjectPath)}";

        var inferred = compilerVersion is null
            || languageVersion is null
            || apiVersion is null
            || jvmTarget is null
            || (ReadOption(options, "kotlin.configurationInferred") is { } inferredValue
                && bool.TryParse(inferredValue, out var explicitlyInferred)
                && explicitlyInferred);

        return (
            new KotlinCompilerConfiguration(
                compilerVersion ?? DefaultCompilerVersion,
                languageVersion ?? DefaultLanguageVersion,
                apiVersion ?? DefaultApiVersion,
                jvmTarget ?? DefaultJvmTarget,
                moduleName),
            inferred);
    }

    private static string? ReadOption(
        IReadOnlyDictionary<string, string> options,
        string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string CreateClasspathDiagnostic(SemanticAnalysisRequest request)
    {
        var unresolved = request.UnresolvedClasspathDependencies
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(12)
            .ToArray();
        var summary = unresolved.Length == 0
            ? "Some declared dependencies were not available as explicit resolved classpath entries."
            : $"Unresolved dependencies include: {string.Join(", ", unresolved)}.";
        return $"The Kotlin module declares dependencies, but its complete resolved compile classpath is unavailable. {summary} Only direct artifacts found in local caches were supplied; transitive closure and project-module compiler outputs are not inferred.";
    }

    private SemanticAnalysisRun CreateUnavailableRun(
        SemanticAnalysisRequest request,
        SemanticAnalysisStatus status,
        string code,
        string message,
        string? exceptionType = null) =>
        new()
        {
            RunId = Guid.NewGuid().ToString("N"),
            Language = Language,
            EngineId = AdapterId,
            EngineVersion = DefaultCompilerVersion,
            ProjectPath = request.ProjectPath,
            Status = status,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Diagnostics =
            [
                new AnalysisDiagnostic
                {
                    Code = code,
                    Component = nameof(KotlinSemanticAnalysisAdapter),
                    Message = message,
                    Target = request.ProjectPath,
                    ExceptionType = exceptionType,
                    AffectsCoverage = true
                }
            ]
        };
}
