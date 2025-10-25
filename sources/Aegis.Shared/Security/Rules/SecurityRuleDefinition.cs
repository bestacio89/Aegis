using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules;

/// <summary>
/// Defines a strong, measurable security rule definition with metadata and thresholds.
/// </summary>
public sealed class SecurityRuleDefinition
{
    public string RuleId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public SecurityCategory Category { get; init; }
    public SecuritySeverity Severity { get; init; }
    public RiskLevel RiskLevel { get; init; }

    public double MinScore { get; init; }
    public double MaxScore { get; init; }
    public string ThresholdType { get; init; } = "CVSS";

    public string AttackSurface { get; init; } = "Unknown";
    public string ImpactScope { get; init; } = "Local";

    public string? OwaspId { get; init; }
    public string? CweId { get; init; }
    public VulnerabilityType? Vulnerability { get; init; }

    public string DetectionVector { get; init; } = "Static";
    public string EvidencePattern { get; init; } = string.Empty;
    public string[] AffectedArtifacts { get; init; } = Array.Empty<string>();

    public string Remediation { get; init; } = string.Empty;
    public string ReferenceUrl { get; init; } = string.Empty;
    public string[] Tags { get; init; } = Array.Empty<string>();
}
