using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules;

public static class SecurityRuleSetFactory
{
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
}
