namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Counts and diagnostics describing what the rule engine actually assessed.
/// Rule counts and scoped evaluation counts are separate dimensions.
/// </summary>
public sealed class RuleEvaluationStats
{
    /// <summary>Total number of rule definitions in the registry.</summary>
    public int RulesRegistered { get; init; }

    /// <summary>Supported rules whose applicability conditions match the analyzed scope.</summary>
    public int RulesApplicable { get; init; }

    /// <summary>Rules explicitly disabled by the active policy; excluded from applicability and coverage.</summary>
    public int RulesDisabledByPolicy { get; init; }

    public IReadOnlyDictionary<string, string> DisabledByPolicyReasons { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rules whose declared metric semantics have no supported producer.</summary>
    public int RulesUnsupported { get; init; }

    /// <summary>
    /// Measurements withheld because their producer unit or threshold scale
    /// did not satisfy a verified metric contract.
    /// </summary>
    public int MetricContractFailures { get; init; }

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

    /// <summary>
    /// Facts that emitted at least one metric key known to the rule registry,
    /// but for which no rule could consume the metric because its producer
    /// contract or fact-level applicability did not permit evaluation.
    /// Wrong-scope evidence is tracked separately.
    /// </summary>
    public int FactsWithKnownMetricsWithoutEligibleRule { get; init; }

    /// <summary>
    /// Supported, applicable rules for which the required evidence or a
    /// completed comparison was unavailable. Unsupported, disabled, and
    /// positively inapplicable rules are tracked separately.
    /// </summary>
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

    /// <summary>
    /// Difference between registered rules and the mutually exclusive accounting
    /// categories: supported applicable, unsupported, disabled by policy, and
    /// not applicable. Not-evaluated rules are a subset of supported applicable.
    /// Zero means every registered rule has exactly one classification.
    /// </summary>
    public int RuleAccountingDifference =>
        RulesRegistered
        - (RulesApplicable + RulesUnsupported + RulesDisabledByPolicy + RulesNotApplicable.Count);

    public IReadOnlyList<RuleCheckBucket> CheckBuckets { get; init; }
        = Array.Empty<RuleCheckBucket>();

    public IReadOnlyDictionary<string, int> RulesEvaluatedByDomain { get; init; }
        = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Percentage of supported, applicable rules with at least one completed evaluation.
    /// Rules that do not apply to the analyzed project, are disabled by policy, or
    /// lack a supported producer are excluded from this project-level denominator.
    /// Unsupported producer contracts are reported separately by RuleCapabilityCoverage.
    /// </summary>
    public double EvaluationCoverage
    {
        get
        {
            return RulesApplicable <= 0
                ? 0.0
                : Math.Clamp((double)RulesEvaluated / RulesApplicable * 100.0, 0.0, 100.0);
        }
    }

    /// <summary>
    /// Percentage of registered rules for which the engine has a supported producer contract.
    /// This is an engine capability measure, not project-specific evaluation coverage.
    /// </summary>
    /// <summary>
    /// Percentage of all registered rules that received at least one completed evaluation.
    /// This is a registry-wide diagnostic measure, not project applicability coverage.
    /// </summary>
    public double RegisteredRuleEvaluationCoverage => RulesRegistered <= 0
        ? 0.0
        : Math.Clamp((double)RulesEvaluated / RulesRegistered * 100.0, 0.0, 100.0);

    public double RuleCapabilityCoverage
    {
        get
        {
            return RulesRegistered <= 0
                ? 0.0
                : Math.Clamp(
                    (double)(RulesRegistered - RulesUnsupported) / RulesRegistered * 100.0,
                    0.0,
                    100.0);
        }
    }
}