namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Counts and diagnostics describing what the rule engine actually assessed.
/// Rule counts and scoped evaluation counts are separate dimensions.
/// </summary>
public sealed class RuleEvaluationStats
{
    public int RulesRegistered { get; init; }

    public int RulesApplicable { get; init; }

    /// <summary>Rules whose declared metric semantics have no supported producer.</summary>
    public int RulesUnsupported { get; init; }

    public IReadOnlyDictionary<string, string> UnsupportedRuleReasons { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Distinct applicable rules with at least one completed evaluation.
    /// A completed evaluation may pass or violate.
    /// </summary>
    public int RulesEvaluated { get; init; }

    public int RulesPassed { get; init; }

    public int RulesViolated { get; init; }

    public int RulesWithErrors { get; init; }

    public int EvaluationsPassed { get; init; }

    public int EvaluationsViolated { get; init; }

    public int EvaluationsNotEvaluated { get; init; }

    public int EvaluationsInsufficientEvidence { get; init; }

    public int EvaluationsErrored { get; init; }
    /// <summary>Individual rule checks that passed.</summary>
    public int ChecksPassed { get; init; }

    /// <summary>Individual rule checks that produced violations.</summary>
    public int ChecksViolated { get; init; }


    /// <summary>
    /// Number of valid rule-to-measurement comparisons completed.
    /// </summary>
    public int ChecksPerformed { get; init; }

    /// <summary>
    /// Evaluator facts that contain no metric key declared by any applicable rule.
    /// This is a metric-production gap, not an evaluator execution failure.
    /// </summary>
    public int FactsWithoutMatchingScopedMetrics { get; init; }

    /// <summary>
    /// Evaluator facts whose metric keys are known, but only match rules at a
    /// different scope than the fact's declared scope.
    /// </summary>
    public int FactsWithMetricsAtWrongScope { get; init; }

    public IReadOnlyList<string> RulesNotEvaluated { get; init; }
        = Array.Empty<string>();

    public IReadOnlyList<string> RulesNotApplicable { get; init; }
        = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> NotApplicableReasons { get; init; }
        = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> NotEvaluatedReasons { get; init; }
        = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RuleCheckBucket> CheckBuckets { get; init; }
        = Array.Empty<RuleCheckBucket>();

    public IReadOnlyDictionary<string, int> RulesEvaluatedByDomain { get; init; }
        = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Percentage of applicable rules with at least one completed evaluation.
    /// Applicable rules without a supported producer remain in the denominator:
    /// missing capability must reduce coverage rather than disappear from it.
    /// Rules positively classified as not applicable are excluded.
    /// </summary>
    public double EvaluationCoverage
    {
        get
        {
            var applicableRuleCount = RulesApplicable + RulesUnsupported;
            return applicableRuleCount <= 0
                ? 0.0
                : Math.Clamp((double)RulesEvaluated / applicableRuleCount * 100.0, 0.0, 100.0);
        }
    }
}