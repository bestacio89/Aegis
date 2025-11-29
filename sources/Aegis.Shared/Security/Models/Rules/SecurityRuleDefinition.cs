using Aegis.Shared.Security.Enums;

public sealed class SecurityRuleDefinition
{
    // ----- EXISTING FIELDS -----

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

    // ----- NEW ISO / GRC FIELDS -----

    /// <summary>ISO 27001 control (e.g., "A.9.2.3").</summary>
    public string? IsoControl { get; init; }

    /// <summary>ISO clause (e.g., "Clause 6", "Clause 8").</summary>
    public string? IsoClause { get; init; }

    /// <summary>GDPR Article number if applicable.</summary>
    public string? GdprArticle { get; init; }

    /// <summary>NIST SP 800-53 control ID if applicable.</summary>
    public string? NistControl { get; init; }

    /// <summary>CIS Benchmark control mapping (e.g., "CIS-1.1", "CIS-3.5").</summary>
    public string? CisControl { get; init; }

    /// <summary>
    /// Whether this control is required for ISO compliance, or only recommended.
    /// </summary>
    public bool RequiredForCompliance { get; init; } = true;

    /// <summary>
    /// Weight used for control maturity scoring (0.0–1.0).
    /// </summary>
    public double ControlWeight { get; init; } = 1.0;

    /// <summary>
    /// Additional policy or regulatory references.
    /// </summary>
    public string[] GovernanceReferences { get; init; } = Array.Empty<string>();
}
