namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Counts and diagnostics describing what the rule engine actually assessed.
/// Rule counts and scoped evaluation counts are separate dimensions.
/// </summary>
public sealed class RuleEvaluationStats
{
    public int RulesRegistered { get; init; }

    public int RulesApplicable { get; init; }

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

    public double EvaluationCoverage =>
        RulesApplicable == 0
            ? 1.0
            : (double)RulesEvaluated / RulesApplicable;
}