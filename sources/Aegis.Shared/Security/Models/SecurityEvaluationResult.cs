using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models.Rules;
using System.Collections.ObjectModel;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Represents the aggregate outcome of a single security evaluation pass.
/// </summary>
public sealed class SecurityEvaluationResult
{
    /// <summary>
    /// Unique identifier for this evaluation.
    /// </summary>
    public Guid EvaluationId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Optional identifier for the project or component under evaluation.
    /// </summary>
    public string TargetContext { get; init; } = string.Empty;

    /// <summary>
    /// Category of the evaluation (Application, IaC, Network, etc.).
    /// </summary>
    public SecurityCategory Category { get; init; }

    /// <summary>
    /// Collection of individual rule results.
    /// </summary>
    public IReadOnlyCollection<SecurityRuleResult> RuleResults { get; init; } = Array.Empty<SecurityRuleResult>();

    /// <summary>
    /// Computed average numeric score (0–10) across all rule results.
    /// </summary>
    public double AverageScore => RuleResults.Any()
        ? Math.Round(RuleResults.Average(r => r.Score), 2)
        : 0.0;

    /// <summary>
    /// Maximum severity among all rule results.
    /// </summary>
    public SecuritySeverity MaxSeverity => RuleResults.Any()
        ? RuleResults.MaxBy(r => r.Severity)!.Severity
        : SecuritySeverity.Info;

    /// <summary>
    /// Total count of violations grouped by severity.
    /// </summary>
    public IReadOnlyDictionary<SecuritySeverity, int> SeverityDistribution =>
        RuleResults
            .GroupBy(r => r.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Indicates whether this evaluation has any critical findings.
    /// </summary>
    public bool HasCriticalFindings => RuleResults.Any(r => r.Severity == SecuritySeverity.Critical);

    /// <summary>
    /// The UTC timestamp when the evaluation was completed.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Total number of rules executed.
    /// </summary>
    public int TotalRules => RuleResults.Count;

    /// <summary>
    /// Total number of rules that passed without violation.
    /// </summary>
    public int PassedRules => RuleResults.Count(r => r.Score <= 1.9);

    /// <summary>
    /// Total number of rules that failed.
    /// </summary>
    public int FailedRules => TotalRules - PassedRules;

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

    /// <summary>
    /// Generates a human-readable summary string.
    /// </summary>
    public override string ToString() =>
        $"[{Category}] Eval {EvaluationId} — {FailedRules}/{TotalRules} violations (Max: {MaxSeverity}, Avg: {AverageScore:F2})";
}
