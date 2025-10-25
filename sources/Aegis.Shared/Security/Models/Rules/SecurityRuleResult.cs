using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules;

/// <summary>
/// Represents the result of a single evaluated security rule.
/// </summary>
public sealed class SecurityRuleResult
{
    public string RuleId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public SecurityCategory Category { get; init; }
    public VulnerabilityType Vulnerability { get; init; }
    public SecuritySeverity Severity { get; init; }
    public RiskLevel Risk { get; init; }

    /// <summary>
    /// Numeric risk score between 0–10 based on severity thresholds.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// The file, resource, or entity where the issue was detected.
    /// </summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// Optional location (e.g., line number, resource path, URI, etc.).
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Diagnostic message or matched pattern.
    /// </summary>
    public string? Evidence { get; init; }

    /// <summary>
    /// Optional CWE/OWASP reference.
    /// </summary>
    public string? Reference { get; init; }

    /// <summary>
    /// UTC timestamp of when this result was generated.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Returns a color corresponding to the severity for UI bindings.
    /// </summary>
    public string SeverityColor => SecuritySeverityThresholds.GetColor(Severity);

    /// <summary>
    /// Returns the score description range (Info, Low, etc.)
    /// </summary>
    public string SeverityDescription => SecuritySeverityThresholds.GetRange(Severity).Description;

    /// <summary>
    /// Creates a standardized, ready-to-log summary string.
    /// </summary>
    public override string ToString() =>
        $"[{Severity}] {RuleId} — {Title} ({Target}) Score={Score:F1}";
}
