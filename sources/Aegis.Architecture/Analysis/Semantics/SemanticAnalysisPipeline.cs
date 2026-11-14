using Aegis.Architecture.Analysis.Classification;
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
            .OrderBy(analyzer => analyzer.ExecutionOrder)
            .ThenBy(analyzer => analyzer.Language, StringComparer.OrdinalIgnoreCase)
            .ThenBy(analyzer => analyzer.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
        _logger = logger;
    }

    public async Task AnalyzeAsync(
        ProjectArchitectureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await using var executionContext = new SemanticAnalysisExecutionContext(_logger);

        foreach (var analyzer in _analyzers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedAtUtc = DateTimeOffset.UtcNow;

            try
            {
                await analyzer.AnalyzeAsync(context, executionContext, cancellationToken)
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

        RecordMissingLanguageAnalyzers(context);
        ModuleRoleDetector.Analyze(context.Modules);

        var architectureMap = await ArchitectureMapConfigurationLoader.LoadAsync(
            context.RootPath,
            cancellationToken).ConfigureAwait(false);

        ProjectArchitectureComponents(context, architectureMap.Configuration);
        context.ArchitectureClassificationDiagnostics.InsertRange(0, architectureMap.Diagnostics);
    }

    private void RecordMissingLanguageAnalyzers(ProjectArchitectureContext context)
    {
        var registeredLanguages = _analyzers
            .Select(analyzer => analyzer.Language)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var discoveredLanguages = context.ProductionModules
            .SelectMany(module => module.Languages)
            .Append(context.Language)
            .Where(language => !string.IsNullOrWhiteSpace(language)
                && !language.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var language in discoveredLanguages)
        {
            if (registeredLanguages.Contains(language)
                || context.SemanticAnalysisRuns.Any(run =>
                    run.Language.Equals(language, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            context.SemanticAnalysisRuns.Add(new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = language,
                EngineId = nameof(SemanticAnalysisPipeline),
                ProjectPath = context.RootPath,
                Status = SemanticAnalysisStatus.Unsupported,
                StartedAtUtc = now,
                CompletedAtUtc = now,
                Diagnostics =
                [
                    new AnalysisDiagnostic
                    {
                        Code = "SemanticProjectAnalyzerNotRegistered",
                        Component = nameof(SemanticAnalysisPipeline),
                        Message = $"No semantic project analyzer is registered for discovered language '{language}'.",
                        Target = context.RootPath,
                        AffectsCoverage = true
                    }
                ]
            });

            _logger.LogWarning(
                "No semantic project analyzer is registered for discovered language {Language} in {ProjectPath}.",
                language,
                context.RootPath);
        }
    }

    /// <summary>
    /// Projects all available language-provider semantic runs into the shared
    /// language-native architecture component graph.
    /// </summary>
    public static void ProjectArchitectureComponents(
        ProjectArchitectureContext context,
        ArchitectureMapConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        SemanticArchitectureComponentDetector.Analyze(context);
        ArchitecturalComponentClassifier.Classify(context, configuration);
        ArchitecturalComponentDependencyProjector.Project(context);
        ArchitectureDependencyCycleAnalyzer.Analyze(context);
    }
}
