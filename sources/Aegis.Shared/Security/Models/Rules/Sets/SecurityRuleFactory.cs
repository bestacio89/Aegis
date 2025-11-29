using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules.Sets;

public static class SecurityRuleSetFactory
{
    // =====================================================================================
    // ORIGINAL FACTORY (Backward Compatible)
    // =====================================================================================
    public static SecurityRuleDefinition Create(
        string id,
        string title,
        string desc,
        SecurityCategory category,
        SecuritySeverity severity,
        RiskLevel risk,
        string? owasp,
        string? cwe,
        VulnerabilityType vuln,
        string[] tags,
        string pattern,
        string surface,
        string remediation,
        string reference,
        string detection = "Static",
        string[]? files = null)
    {
        var (min, max, _) = SecuritySeverityThresholds.GetRange(severity);

        return new SecurityRuleDefinition
        {
            RuleId = id,
            Title = title,
            Description = desc,
            Category = category,
            Severity = severity,
            RiskLevel = risk,
            OwaspId = owasp,
            CweId = cwe,
            Vulnerability = vuln,
            Tags = tags,
            EvidencePattern = pattern,
            AttackSurface = surface,
            Remediation = remediation,
            ReferenceUrl = reference,
            ThresholdType = "CVSS",
            MinScore = min,
            MaxScore = max,
            DetectionVector = detection,
            AffectedArtifacts = files ?? Array.Empty<string>()
        };
    }


    // =====================================================================================
    // NEW ISO-READY FACTORY (Full Governance Metadata)
    // =====================================================================================
    public static SecurityRuleDefinition CreateISO(
        string id,
        string title,
        string desc,
        SecurityCategory category,
        SecuritySeverity severity,
        RiskLevel risk,
        string? owasp,
        string? cwe,
        VulnerabilityType vuln,
        string[] tags,
        string pattern,
        string surface,
        string remediation,
        string reference,

        // ISO / GRC Extensions ↓↓↓
        string? isoControl = null,
        string? isoClause = null,
        string? cis = null,
        string? nist = null,
        string? gdpr = null,
        bool required = true,
        double weight = 1.0,
        string[]? governanceRefs = null,

        string detection = "Static",
        string[]? files = null)
    {
        var (min, max, _) = SecuritySeverityThresholds.GetRange(severity);

        return new SecurityRuleDefinition
        {
            RuleId = id,
            Title = title,
            Description = desc,
            Category = category,
            Severity = severity,
            RiskLevel = risk,
            OwaspId = owasp,
            CweId = cwe,
            Vulnerability = vuln,

            Tags = tags,
            EvidencePattern = pattern,
            AttackSurface = surface,
            Remediation = remediation,
            ReferenceUrl = reference,
            AffectedArtifacts = files ?? Array.Empty<string>(),

            ThresholdType = "CVSS",
            MinScore = min,
            MaxScore = max,
            DetectionVector = detection,

            // -------- ISO / GRC Metadata Fields ---------
            IsoControl = isoControl,
            IsoClause = isoClause,
            CisControl = cis,
            NistControl = nist,
            GdprArticle = gdpr,
            RequiredForCompliance = required,
            ControlWeight = weight,
            GovernanceReferences = governanceRefs ?? Array.Empty<string>()
        };
    }
}
