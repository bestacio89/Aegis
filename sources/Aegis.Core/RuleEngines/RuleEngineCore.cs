using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Aegis.Shared.Models.Rules;
using Aegis.Shared.Rules;
using Microsoft.Extensions.Logging;

namespace Aegis.Core.RuleEngines;

/// <summary>
/// 🧠 Core evaluation engine that interprets evaluator metrics against governance rule definitions.
/// Transforms EvaluatorResults → RuleResults according to registered RuleDefinitions.
/// </summary>
public sealed class RuleEngineCore
{
    private readonly ILogger<RuleEngineCore> _logger;
    private readonly IReadOnlyList<RuleDefinition> _rules;

    public RuleEngineCore(ILogger<RuleEngineCore> logger)
    {
        _logger = logger;
        _rules = RuleRegistry.All;
    }

    /// <summary>
    /// Evaluates all collected evaluator results (facts) against the active rule registry.
    /// </summary>
    public IEnumerable<RuleResult> Evaluate(IEnumerable<EvaluatorResult> facts)
    {
        var results = new List<RuleResult>();
        int totalChecks = 0;

        foreach (var fact in facts)
        {
            foreach (var (metricKey, value) in fact.Metrics)
            {
                var applicable = _rules.Where(r =>
                    r.MetricKey.Equals(metricKey, StringComparison.OrdinalIgnoreCase));

                foreach (var rule in applicable)
                {
                    totalChecks++;

                    bool violated = rule.Operator switch
                    {
                        ComparisonOperator.LessThan => value < rule.Threshold,
                        ComparisonOperator.GreaterThan => value > rule.Threshold,
                        ComparisonOperator.Equal => Math.Abs(value - rule.Threshold) < 0.0001,
                        ComparisonOperator.NotEqual => Math.Abs(value - rule.Threshold) > 0.0001,
                        _ => false
                    };

                    if (violated)
                    {
                        results.Add(new RuleResult(
                            rule.Id,
                            rule.Name,
                            Enum.TryParse<RuleCategory>(rule.Category, out var cat)
                                ? cat
                                : RuleCategory.General,
                            rule.Severity,
                            fact.Target,
                            fact.Metadata?["Namespace"],
                            $"[{fact.Source}] Metric '{metricKey}' = {value:0.##}, threshold = {rule.Threshold:0.##}. {rule.Recommendation}",
                            DateTimeOffset.UtcNow
                        ));
                    }
                }
            }
        }

        _logger.LogInformation("🧩 RuleEngineCore evaluated {Facts} facts → {Results} violations ({Checks} checks).",
            facts.Count(), results.Count, totalChecks);

        return results;
    }
}
