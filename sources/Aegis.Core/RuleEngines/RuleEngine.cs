using Aegis.Architecture.Aggregation;
using Aegis.Architecture.Scoring;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.RuleEngines;

public sealed class RuleEngine
{
    private readonly IEnumerable<IEvaluator> _evaluators;
    private readonly RuleEngineCore _core;
    private readonly RuleWeightingEngine _weighting;
    private readonly CrossEvaluatorAggregator _aggregator;
    private readonly ILogger<RuleEngine> _logger;

    private AegisArchitecturePolicy? _policy;

    public RuleEngine(
        IEnumerable<IEvaluator> evaluators,
        RuleEngineCore core,
        RuleWeightingEngine weighting,
        CrossEvaluatorAggregator aggregator,
        ILogger<RuleEngine> logger)
    {
        _evaluators = evaluators;
        _core = core;
        _weighting = weighting;
        _aggregator = aggregator;
        _logger = logger;
    }

    public void ApplyPolicy(AegisArchitecturePolicy policy)
    {
        _policy = policy;
        _logger.LogInformation("⚙️ Applying Aegis policy → {Version}", policy.Version ?? "1.0");

        foreach (var evaluator in _evaluators)
        {
            var name = evaluator.Name.ToLowerInvariant();
            if (name.Contains("architecture")) ApplyEvaluatorConfig(evaluator, policy.Architecture);
            else if (name.Contains("naming")) ApplyEvaluatorConfig(evaluator, policy.Naming);
            else if (name.Contains("dependency")) ApplyEvaluatorConfig(evaluator, policy.Dependency);
            else if (name.Contains("security")) ApplyEvaluatorConfig(evaluator, policy.Security);
            else if (name.Contains("performance")) ApplyEvaluatorConfig(evaluator, policy.Performance);
            else if (name.Contains("persistence") || name.Contains("repository")) ApplyEvaluatorConfig(evaluator, policy.Repository);
            else if (name.Contains("frontend")) ApplyEvaluatorConfig(evaluator, policy.Frontend);
            else if (name.Contains("node")) ApplyEvaluatorConfig(evaluator, policy.Node);
            else if (name.Contains("complexity")) ApplyEvaluatorConfig(evaluator, policy.Complexity);
            else if (name.Contains("maintainability")) ApplyEvaluatorConfig(evaluator, policy.Maintainability);
            else if (name.Contains("cohesion")) ApplyEvaluatorConfig(evaluator, policy.Cohesion);
            else if (name.Contains("coupling")) ApplyEvaluatorConfig(evaluator, policy.Coupling);
            else if (name.Contains("logging")) ApplyEvaluatorConfig(evaluator, policy.Logging);
            else if (name.Contains("transaction")) ApplyEvaluatorConfig(evaluator, policy.Transaction);
        }

        _weighting.ApplyPolicy(policy);
        _aggregator.ApplyPolicy(policy);
        _core.ApplyPolicy(policy);
    }

    private void ApplyEvaluatorConfig(IEvaluator evaluator, object policySection)
    {
        var enabledProp = policySection.GetType().GetProperty("Enabled");
        var weightProp = policySection.GetType().GetProperty("Weight");

        if (enabledProp?.GetValue(policySection) is bool enabled) evaluator.IsEnabled = enabled;
        if (weightProp?.GetValue(policySection) is double weight && weight > 0) evaluator.WeightFactor = weight;
    }

    /// <summary>
    /// Executes the analysis pipeline with an optional path filter to exclude specific directories.
    /// </summary>
    public async Task<AegisArchitectureReport> RunAsync(
        string projectPath,
        ProjectArchitectureContext context,
        CancellationToken token = default,
        Func<string, bool>? pathFilter = null)
    {
        var report = new AegisArchitectureReport
        {
            ProjectName = Path.GetFileName(projectPath),
            ProjectPath = projectPath,
            Language = context.Language,
            Framework = context.Framework,
            ScanDate = DateTimeOffset.UtcNow
        };

        _logger.LogInformation("🚀 Starting Aegis scan for {Project} [Filter Applied: {IsFiltered}]",
            report.ProjectName, pathFilter != null);

        var allFacts = new List<ArchitectureEvaluatorResult>();

        foreach (var evaluator in _evaluators.Where(e => e.IsEnabled))
        {
            if (!evaluator.SupportedLanguages.Contains(context.Language) && !evaluator.SupportedLanguages.Contains("*"))
                continue;

            try
            {
                // We pass the filter into the evaluation process.
                // Note: Ensure your IEvaluator implementations are updated to respect this filter if they scan files manually.
                var results = await evaluator.EvaluateAsync(projectPath, context, token);

                // If the evaluator returns results for all files, apply the filter here as a safeguard:
                allFacts.AddRange(pathFilter != null
                    ? results.Where(r => pathFilter(r.Target))
                    : results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Evaluator {Name} failed.", evaluator.Name);
            }
        }

        var ruleResults = _core.Evaluate(context, allFacts).ToList();
        foreach (var _ in _weighting.ApplyWeights(ruleResults)) { }

        foreach (var result in ruleResults) report.Results.Add(result);
        foreach (var fact in allFacts) report.Facts.Add(fact);

        report.TotalFilesScanned = context.FileCount;
        report.ComputeCompliance();

        return report;
    }
}