
using Aegis.Architecture.Aggregation;
using Aegis.Architecture.Scoring;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.RuleEngines;

/// <summary>
/// Orchestrates evaluator execution, rule interpretation, weighting,
/// aggregation, and report enrichment.
/// </summary>
public sealed class RuleEngine
{
    private readonly IReadOnlyCollection<IEvaluator> _evaluators;
    private readonly RuleEngineCore _core;
    private readonly RuleWeightingEngine _weighting;
    private readonly CrossEvaluatorAggregator _aggregator;
    private readonly ILogger<RuleEngine> _logger;

    public RuleEngine(
        IEnumerable<IEvaluator> evaluators,
        RuleEngineCore core,
        RuleWeightingEngine weighting,
        CrossEvaluatorAggregator aggregator,
        ILogger<RuleEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(evaluators);
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(weighting);
        ArgumentNullException.ThrowIfNull(aggregator);
        ArgumentNullException.ThrowIfNull(logger);

        _evaluators = evaluators.ToArray();
        _core = core;
        _weighting = weighting;
        _aggregator = aggregator;
        _logger = logger;

        foreach (var evaluator in _evaluators)
        {
            _logger.LogInformation(
                "Registered evaluator: {Evaluator}",
                evaluator.Name);
        }

        _logger.LogInformation(
            "Aegis RuleEngine initialized with {Count} evaluators.",
            _evaluators.Count);
    }

    public void ApplyPolicy(AegisArchitecturePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        _core.ApplyPolicy(policy);
        _weighting.ApplyPolicy(policy);
        _aggregator.ApplyPolicy(policy);

        _logger.LogInformation(
            "Aegis policy applied. Version={Version}. Evaluators={Count}",
            policy.Version ?? "Unknown",
            _evaluators.Count);
    }

    private const double EvaluatorsStart = 10;
    private const double EvaluatorsEnd = 80;

    /// <summary>
    /// Executes the analysis pipeline.
    /// Evaluator failures are recorded without discarding successful results
    /// from other evaluators.
    /// </summary>
    public async Task<AegisArchitectureReport> RunAsync(
        string projectPath,
        ProjectArchitectureContext context,
        CancellationToken token = default,
        Func<string, bool>? pathFilter = null,
        IProgress<AnalysisProgressInfo>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogInformation(
            "Starting Aegis scan for {Project}",
            context.ProjectName);

        var facts = new List<ArchitectureEvaluatorResult>();
        var diagnostics = new List<AnalysisDiagnostic>();
        var applicable = new List<IEvaluator>();

        foreach (var evaluator in _evaluators)
        {
            if (SupportsContext(evaluator, context))
            {
                applicable.Add(evaluator);
                continue;
            }

            _logger.LogDebug(
                "Evaluator {Evaluator} skipped. Context not supported: {Language}/{Framework}",
                evaluator.Name,
                context.Language,
                context.Framework);
        }

        for (var i = 0; i < applicable.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var evaluator = applicable[i];

            var percent = applicable.Count == 0
                ? EvaluatorsEnd
                : EvaluatorsStart +
                  (EvaluatorsEnd - EvaluatorsStart) * i / applicable.Count;

            Report(
                progress,
                percent,
                "Evaluating",
                $"{evaluator.Name} ({i + 1}/{applicable.Count})");

            _logger.LogInformation(
                "Running evaluator: {Evaluator} ({Index}/{Total})",
                evaluator.Name,
                i + 1,
                applicable.Count);

            try
            {
                var results = await evaluator.EvaluateAsync(
                    projectPath,
                    context,
                    token);

                if (results is null)
                {
                    diagnostics.Add(new AnalysisDiagnostic
                    {
                        Code = "EvaluatorReturnedNull",
                        Component = evaluator.Name,
                        Message =
                            "The evaluator returned a null result collection.",
                        AffectsCoverage = true
                    });

                    _logger.LogWarning(
                        "Evaluator {Evaluator} returned a null result collection.",
                        evaluator.Name);

                    continue;
                }

                foreach (var result in results)
                {
                    token.ThrowIfCancellationRequested();

                    if (result is null)
                    {
                        diagnostics.Add(new AnalysisDiagnostic
                        {
                            Code = "EvaluatorReturnedNullFact",
                            Component = evaluator.Name,
                            Message =
                                "The evaluator returned a null fact.",
                            AffectsCoverage = true
                        });

                        continue;
                    }

                    if (pathFilter is not null &&
                        !pathFilter(result.Target))
                    {
                        continue;
                    }

                    facts.Add(result);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "EvaluatorFailed",
                    Component = evaluator.Name,
                    Message =
                        $"Evaluator '{evaluator.Name}' failed during analysis.",
                    ExceptionType = ex.GetType().FullName,
                    AffectsCoverage = true
                });

                _logger.LogError(
                    ex,
                    "Evaluator {Evaluator} failed. The report will contain a coverage diagnostic.",
                    evaluator.Name);
            }
        }

        _logger.LogInformation(
            "{Count} facts generated by {EvaluatorCount} applicable evaluators.",
            facts.Count,
            applicable.Count);

        Report(
            progress,
            EvaluatorsEnd,
            "Interpreting",
            $"{facts.Count:N0} facts against the rule set");

        var outcome = _core.EvaluateDetailed(context, facts);
        var ruleResults = outcome.Results.ToList();

        _logger.LogInformation(
            "{Count} rule violations generated.",
            ruleResults.Count);

        Report(
            progress,
            88,
            "Weighting",
            $"{ruleResults.Count:N0} violations");

        // Weight findings only. Passing evaluations are not findings.
        var weightedResults = _weighting
            .ApplyWeights(ruleResults)
            .ToList();

        Report(
            progress,
            90,
            "Aggregating",
            "Domains and compliance");

        var report = _aggregator.Aggregate(
            weightedResults,
            facts,
            context,
            outcome.Stats);

        // Persist every scoped evaluation, including passing evaluations.
        foreach (var evaluation in outcome.Evaluations)
        {
            report.Evaluations.Add(evaluation);
        }

        // Preserve evaluator failures in the report instead of relying
        // on log output, which consumers cannot reliably inspect.
        foreach (var diagnostic in diagnostics)
        {
            report.Diagnostics.Add(diagnostic);
        }

        _logger.LogInformation(
            "Aegis scan completed. Violations={Violations}, Checks={Checks}, " +
            "RulesEvaluated={Evaluated}/{Registered}, Diagnostics={Diagnostics}",
            report.TotalViolations,
            outcome.Stats.ChecksPerformed,
            outcome.Stats.RulesEvaluated,
            outcome.Stats.RulesRegistered,
            diagnostics.Count);

        if (outcome.Stats.ChecksPerformed == 0)
        {
            _logger.LogWarning(
                "No rule comparisons were completed against the {Registered} registered rules. " +
                "Compliance and health figures may not be meaningful.",
                outcome.Stats.RulesRegistered);
        }

        if (diagnostics.Any(diagnostic => diagnostic.AffectsCoverage))
        {
            _logger.LogWarning(
                "The report was generated with incomplete evaluator coverage. " +
                "Coverage-affecting diagnostics={Count}.",
                diagnostics.Count(diagnostic => diagnostic.AffectsCoverage));
        }

        Report(
            progress,
            92,
            "Analysis complete",
            null);

        return report;
    }

    private static void Report(
        IProgress<AnalysisProgressInfo>? progress,
        double percent,
        string stage,
        string? detail) =>
        progress?.Report(
            new AnalysisProgressInfo(percent, stage, detail));

    private static bool SupportsContext(
        IEvaluator evaluator,
        ProjectArchitectureContext context)
    {
        if (!SupportsLanguage(evaluator, context.Language))
        {
            return false;
        }

        // A repository context may contain several valid framework signals.
        // The primary framework is descriptive, not an exclusive allow-list.
        var frameworks = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Framework))
            frameworks.Add(context.Framework);
        if (context.DetectedFrameworks is not null)
            frameworks.AddRange(context.DetectedFrameworks.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (frameworks.Count == 0)
            return SupportsFramework(evaluator, context.Framework);

        return frameworks.Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(framework => SupportsFramework(evaluator, framework));
    }

    private static bool SupportsLanguage(
        IEvaluator evaluator,
        string language)
    {
        return evaluator.SupportedLanguages.Contains(
                   "*",
                   StringComparer.OrdinalIgnoreCase)
               ||
               evaluator.SupportedLanguages.Contains(
                   language,
                   StringComparer.OrdinalIgnoreCase);
    }

    private static bool SupportsFramework(
        IEvaluator evaluator,
        string? framework)
    {
        if (evaluator.SupportedFrameworks.Contains(
                "*",
                StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (evaluator.SupportedFrameworks.Contains(
                "LanguageAgnostic",
                StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(framework))
        {
            return true;
        }

        foreach (var supported in evaluator.SupportedFrameworks)
        {
            if (framework.Equals(
                    supported,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsDotNetFamily(framework) &&
                IsDotNetFamily(supported))
            {
                return true;
            }

            if (framework.Contains(
                    "ASP.NET",
                    StringComparison.OrdinalIgnoreCase) &&
                supported.Contains(
                    "ASP.NET",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDotNetFamily(string framework)
    {
        return framework.Contains(
                   ".NET",
                   StringComparison.OrdinalIgnoreCase)
               ||
               framework.Contains(
                   "EF Core",
                   StringComparison.OrdinalIgnoreCase)
               ||
               framework.Contains(
                   "ASP.NET",
                   StringComparison.OrdinalIgnoreCase);
    }
}
