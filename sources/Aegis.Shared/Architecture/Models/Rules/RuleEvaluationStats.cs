namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// What the rule engine actually checked. Without this, "0 violations" cannot be told apart from
/// "nothing was evaluated": rule results only list failures.
/// </summary>
public sealed class RuleEvaluationStats
{
    /// <summary>Enabled rules known to the engine.</summary>
    public int RulesRegistered { get; init; }

    /// <summary>Rules that were compared against at least one measurement from this project.</summary>
    public int RulesEvaluated { get; init; }

    /// <summary>Total rule-versus-measurement comparisons performed.</summary>
    public int ChecksPerformed { get; init; }

    /// <summary>Registered rules for which no evaluator produced a matching measurement.</summary>
    public IReadOnlyList<string> RulesNotEvaluated { get; init; } = Array.Empty<string>();

    /// <summary>Rules excluded because the detected project technology does not match the rule's declared scope.</summary>
    public IReadOnlyList<string> RulesNotApplicable { get; init; } = Array.Empty<string>();

    /// <summary>Reason each rule was excluded as not applicable.</summary>
    public IReadOnlyDictionary<string, string> NotApplicableReasons { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Checks per domain, severity and category (the denominators for compliance).</summary>
    public IReadOnlyList<RuleCheckBucket> CheckBuckets { get; init; } = Array.Empty<RuleCheckBucket>();

    /// <summary>Distinct rules evaluated per domain.</summary>
    public IReadOnlyDictionary<string, int> RulesEvaluatedByDomain { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}