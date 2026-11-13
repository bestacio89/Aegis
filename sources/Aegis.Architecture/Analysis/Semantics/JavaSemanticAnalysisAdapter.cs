using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>Maps Eclipse JDT worker output into the shared semantic-analysis contract.</summary>
public sealed class JavaSemanticAnalysisAdapter : ISemanticAnalysisAdapter
{
    private static readonly IReadOnlyCollection<string> Capabilities =
        Array.AsReadOnly(new[] { "symbols", "relationships", "compiler-diagnostics" });

    private readonly IJavaWorkerClient _workerClient;

    public JavaSemanticAnalysisAdapter(IJavaWorkerClient workerClient)
    {
        ArgumentNullException.ThrowIfNull(workerClient);
        _workerClient = workerClient;
    }

    public string AdapterId => "eclipse-jdt";

    public string Language => "Java";

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
        ArgumentNullException.ThrowIfNull(request);
        if (!CanAnalyze(request))
            throw new ArgumentException("The request does not describe an analyzable Java module.", nameof(request));

        if (!_workerClient.IsConfigured)
        {
            return CreateUnavailableRun(
                request,
                SemanticAnalysisStatus.Unsupported,
                "JavaWorkerNotConfigured",
                "The Eclipse JDT worker is not deployed. Configure its private Java runtime, worker classes and dependency libraries.");
        }

        SemanticAnalysisRun run;
        try
        {
            run = await _workerClient.AnalyzeAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CreateUnavailableRun(
                request,
                SemanticAnalysisStatus.Failed,
                "JavaSemanticAnalysisFailed",
                exception.Message,
                exception.GetType().Name);
        }

        if (!request.HasDeclaredDependencies || request.ClasspathResolutionComplete)
            return run;

        return new SemanticAnalysisRun
        {
            RunId = run.RunId,
            Language = run.Language,
            EngineId = run.EngineId,
            EngineVersion = run.EngineVersion,
            ProjectPath = run.ProjectPath,
            Status = SemanticAnalysisStatus.PartiallyCompleted,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            AnalyzedFileCount = run.AnalyzedFileCount,
            SkippedFileCount = run.SkippedFileCount,
            Capabilities = run.Capabilities,
            Symbols = run.Symbols,
            Relationships = run.Relationships,
            Diagnostics =
            [
                .. run.Diagnostics,
                new AnalysisDiagnostic
                {
                    Code = "JavaExternalClasspathUnavailable",
                    Component = nameof(JavaSemanticAnalysisAdapter),
                    Message = CreateClasspathDiagnostic(request),
                    Target = request.ProjectPath,
                    AffectsCoverage = true
                }
            ]
        };
    }

    private static string CreateClasspathDiagnostic(SemanticAnalysisRequest request)
    {
        var unresolvedArtifacts = request.UnresolvedClasspathDependencies
            .Where(dependency => !dependency.StartsWith("project:", StringComparison.Ordinal))
            .ToArray();
        var unresolvedProjects = request.UnresolvedClasspathDependencies
            .Where(dependency => dependency.StartsWith("project:", StringComparison.Ordinal))
            .ToArray();
        var artifactSummary = unresolvedArtifacts.Length == 0
            ? "No direct external artifact was missing from the local cache."
            : $"{unresolvedArtifacts.Length} direct external artifact(s) were not found in the local cache: {string.Join(", ", unresolvedArtifacts.Take(10))}.";
        var projectSummary = unresolvedProjects.Length == 0
            ? "No unresolved project-module reference was reported."
            : $"{unresolvedProjects.Length} project-module reference(s) remain incomplete as classpath evidence; declared in-workspace Java source roots are supplied for binding-only visibility where available, but compiled outputs and non-source project artifacts are not inferred.";
        return $"The module declares dependencies, but its complete resolved module classpath is unavailable. {artifactSummary} {projectSummary} Transitive external artifact closure remains unresolved.";
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
            EngineVersion = "3.47.0",
            ProjectPath = request.ProjectPath,
            Status = status,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Diagnostics =
            [
                new AnalysisDiagnostic
                {
                    Code = code,
                    Component = nameof(JavaSemanticAnalysisAdapter),
                    Message = message,
                    Target = request.ProjectPath,
                    ExceptionType = exceptionType,
                    AffectsCoverage = true
                }
            ]
        };
}
