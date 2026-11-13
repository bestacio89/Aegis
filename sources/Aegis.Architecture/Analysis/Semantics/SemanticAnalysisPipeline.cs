using Aegis.Architecture.Analysis.Detectors;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Executes the registered language-specific semantic project analyzers in a
/// deterministic order while keeping failures isolated to their language provider.
/// </summary>
public sealed class SemanticAnalysisPipeline
{
    private readonly IReadOnlyList<ISemanticAnalysisProjectAnalyzer> _analyzers;
    private readonly ILogger<SemanticAnalysisPipeline> _logger;

    public SemanticAnalysisPipeline(
        IEnumerable<ISemanticAnalysisProjectAnalyzer> analyzers,
        ILogger<SemanticAnalysisPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(analyzers);
        ArgumentNullException.ThrowIfNull(logger);

        _analyzers = analyzers
            .OrderBy(analyzer => analyzer.Language, StringComparer.OrdinalIgnoreCase)
            .ThenBy(analyzer => analyzer.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
        _logger = logger;
    }

    public async Task AnalyzeAsync(
        ProjectArchitectureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var analyzer in _analyzers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedAtUtc = DateTimeOffset.UtcNow;

            try
            {
                await analyzer.AnalyzeAsync(context, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var completedAtUtc = DateTimeOffset.UtcNow;
                _logger.LogError(
                    exception,
                    "Semantic project analysis failed for {Language} at {ProjectPath}.",
                    analyzer.Language,
                    context.RootPath);

                context.SemanticAnalysisRuns.Add(new SemanticAnalysisRun
                {
                    RunId = Guid.NewGuid().ToString("N"),
                    Language = analyzer.Language,
                    EngineId = analyzer.GetType().Name,
                    ProjectPath = context.RootPath,
                    Status = SemanticAnalysisStatus.Failed,
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = completedAtUtc,
                    Diagnostics =
                    [
                        new AnalysisDiagnostic
                        {
                            Code = "SemanticProjectAnalysisFailed",
                            Component = analyzer.Language,
                            Message = $"Semantic analysis failed for {analyzer.Language}: {exception.Message}",
                            Target = context.RootPath,
                            AffectsCoverage = true
                        }
                    ]
                });
            }
        }

        ProjectArchitectureComponents(context);
        ModuleRoleDetector.Analyze(context.Modules);
    }

    /// <summary>
    /// Projects all available language-provider semantic runs into the shared
    /// language-native architecture component graph.
    /// </summary>
    public static void ProjectArchitectureComponents(ProjectArchitectureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SemanticArchitectureComponentDetector.Analyze(context);
    }
}
