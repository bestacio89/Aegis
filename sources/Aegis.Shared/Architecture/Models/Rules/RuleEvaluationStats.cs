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
    /// Facts that emitted one or more metrics, but none of those keys matches a
    /// registered rule's declared metric. This may include informational summary
    /// metrics and is not, by itself, proof of an unknown metric or producer defect.
    /// Facts with no metrics are not counted.
    /// </summary>
    public int FactsWithoutMatchingScopedMetrics { get; init; }

    /// <summary>
    /// Facts whose metric keys match a rule that is applicable to the fact,
    /// but the rule's required scope differs from the fact's declared scope.
    /// </summary>
    public int FactsWithMetricsAtWrongScope { get; init; }

    /// <summary>
    /// Facts that emitted at least one metric key known to the rule registry,
    /// but for which no rule could consume the metric because its producer
    /// contract or fact-level applicability did not permit evaluation.
    /// Wrong-scope evidence is tracked separately.
    /// </summary>
    public int FactsWithKnownMetricsWithoutEligibleRule { get; init; }


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

    /// <summary>Fraction of supported applicable rules with at least one completed evaluation.</summary>
    public double EvaluationCoverage =>
        RulesApplicable <= 0
            ? 0.0
            : Math.Clamp((double)RulesEvaluated / RulesApplicable, 0.0, 1.0);
}