using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Aegis.Shared.Security.Models.Rules;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Represents the aggregate outcome of a single security evaluation pass
/// for a given domain (Application, Network, OS, etc.).
/// </summary>
public sealed class SecurityEvaluationResult
{
    /// <summary>Unique identifier for this evaluation.</summary>
    public Guid EvaluationId { get; init; } = Guid.NewGuid();

    /// <summary>Optional identifier for the project or component under evaluation.</summary>
    public string TargetContext { get; init; } = string.Empty;

    /// <summary>Category of the evaluation (Application, IaC, Network, etc.).</summary>
    public SecurityCategory Category { get; init; }

    /// <summary>
    /// Collection of individual rule-based results.
    /// If this collection is non-empty, the evaluation is considered rule-driven.
    /// </summary>
    public IReadOnlyCollection<SecurityRuleResult> RuleResults { get; init; }
        = Array.Empty<SecurityRuleResult>();

    /// <summary>
    /// Collection of findings produced by heuristic / probe-centric evaluators
    /// when no explicit rule set applies.
    /// </summary>
    public IReadOnlyCollection<SecurityFinding> Findings { get; init; }
        = Array.Empty<SecurityFinding>();

    /// <summary>Indicates whether this evaluation is driven by rule definitions.</summary>
    public bool IsRuleBased => RuleResults.Count > 0;

    /// <summary>
    /// Computed average numeric score (0–10) across all rule results, or a heuristic
    /// score when only findings exist.
    /// </summary>
    public double AverageScore =>
        IsRuleBased
            ? (RuleResults.Any()
                ? Math.Round(RuleResults.Average(r => r.Score), 2)
                : 0.0)
            : Findings.Count == 0
                ? 1.0 // no findings => almost perfect
                : 0.5; // findings present but no scoring model yet

    /// <summary>
    /// Maximum severity among all rule results or findings.
    /// </summary>
    public SecuritySeverity MaxSeverity =>
        IsRuleBased
            ? (RuleResults.Any()
                ? RuleResults.MaxBy(r => r.Severity)!.Severity
                : SecuritySeverity.Info)
            : (Findings.Count == 0
                ? SecuritySeverity.Info
                : Findings.MaxBy(f => f.SeverityLevel)!.SeverityLevel);

    /// <summary>The UTC timestamp when the evaluation was completed.</summary>
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Total number of rules executed (rule-based evaluations only).</summary>
    public int TotalRules => RuleResults.Count;

    /// <summary>
    /// Total number of rules that passed without violation.
    /// By default, we consider a rule "passed" when its score stays below a soft threshold.
    /// </summary>
    public int PassedRules => RuleResults.Count(r => r.Score <= 1.9);

    /// <summary>Total number of rules that failed.</summary>
    public int FailedRules => TotalRules - PassedRules;

    /// <summary>
    /// Total count of violations grouped by severity.
    /// Uses rule results when present; otherwise uses findings.
    /// </summary>
    public IReadOnlyDictionary<SecuritySeverity, int> SeverityDistribution =>
        IsRuleBased
            ? RuleResults
                .GroupBy(r => r.Severity)
                .ToDictionary(g => g.Key, g => g.Count())
            : Findings
                .GroupBy(f => f.SeverityLevel)
                .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Indicates whether this evaluation has any critical findings.</summary>
    public bool HasCriticalFindings =>
        IsRuleBased
            ? RuleResults.Any(r => r.Severity == SecuritySeverity.Critical)
            : Findings.Any(f => f.SeverityLevel == SecuritySeverity.Critical);

    /// <summary>
    /// Derived global risk level based on the highest severity encountered.
    /// </summary>
    public RiskLevel RiskLevel =>
        MaxSeverity switch
        {
            SecuritySeverity.Critical => RiskLevel.Severe,
            SecuritySeverity.High => RiskLevel.High,
            SecuritySeverity.Medium => RiskLevel.Moderate,
            SecuritySeverity.Low => RiskLevel.Low,
            _ => RiskLevel.Information
        };

    /// <summary>Human-readable summary for logs and dashboards.</summary>
    public override string ToString() =>
        $"[{Category}] Eval {EvaluationId} — " +
        $"{FailedRules}/{TotalRules} failed (Max: {MaxSeverity}, Avg: {AverageScore:F2})";
}
