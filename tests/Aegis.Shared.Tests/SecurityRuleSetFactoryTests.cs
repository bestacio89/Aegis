using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models.Rules.Sets;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class SecurityRuleSetFactoryTests
{
    [Fact]
    public void Create_maps_core_fields_and_severity_thresholds()
    {
        var rule = SecurityRuleSetFactory.Create(
            id: "TEST-SEC-001",
            title: "Test security rule",
            desc: "Test description",
            category: SecurityCategory.Network,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A05:2021",
            cwe: "CWE-319",
            vuln: VulnerabilityType.WeakCryptography,
            tags: ["transport", "encryption"],
            pattern: "TLSv1.0",
            surface: "Network",
            remediation: "Require modern TLS.",
            reference: "https://example.invalid/security",
            detection: "Static",
            files: [".json", ".yml"]);

        Assert.Equal("TEST-SEC-001", rule.RuleId);
        Assert.Equal("Test security rule", rule.Title);
        Assert.Equal(SecurityCategory.Network, rule.Category);
        Assert.Equal(SecuritySeverity.High, rule.Severity);
        Assert.Equal(RiskLevel.Severe, rule.RiskLevel);
        Assert.Equal("A05:2021", rule.OwaspId);
        Assert.Equal("CWE-319", rule.CweId);
        Assert.Equal(VulnerabilityType.WeakCryptography, rule.Vulnerability);
        Assert.Equal("TLSv1.0", rule.EvidencePattern);
        Assert.Equal("Require modern TLS.", rule.Remediation);
        Assert.Equal("https://example.invalid/security", rule.ReferenceUrl);
        Assert.Equal(["transport", "encryption"], rule.Tags);
        Assert.Equal([".json", ".yml"], rule.AffectedArtifacts);
        Assert.True(rule.MinScore < rule.MaxScore);
        Assert.Equal("CVSS", rule.ThresholdType);
        Assert.Equal("Static", rule.DetectionVector);
    }

    [Fact]
    public void Create_uses_empty_artifact_list_when_none_is_supplied()
    {
        var rule = SecurityRuleSetFactory.Create(
            id: "TEST-SEC-002",
            title: "Test security rule",
            desc: "Test description",
            category: SecurityCategory.Secrets,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: null,
            cwe: null,
            vuln: VulnerabilityType.HardcodedSecret,
            tags: [],
            pattern: "password",
            surface: "Configuration",
            remediation: "Use a secret store.",
            reference: "https://example.invalid/secrets");

        Assert.NotNull(rule.AffectedArtifacts);
        Assert.Empty(rule.AffectedArtifacts);
    }

    [Fact]
    public void CreateISO_preserves_governance_metadata_and_defaults()
    {
        var rule = SecurityRuleSetFactory.CreateISO(
            id: "TEST-ISO-001",
            title: "Governed security rule",
            desc: "Rule with compliance mappings.",
            category: SecurityCategory.Governance,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A05:2021",
            cwe: "CWE-284",
            vuln: VulnerabilityType.SecurityMisconfiguration,
            tags: ["governance"],
            pattern: "allow-all",
            surface: "Policy",
            remediation: "Restrict the policy.",
            reference: "https://example.invalid/governance",
            isoControl: "A.8.24",
            isoClause: "Clause 8",
            cis: "CIS-4.1",
            nist: "SC-8",
            gdpr: "32",
            required: false,
            weight: 2.5,
            governanceRefs: ["internal-policy-17"]);

        Assert.Equal("A.8.24", rule.IsoControl);
        Assert.Equal("Clause 8", rule.IsoClause);
        Assert.Equal("CIS-4.1", rule.CisControl);
        Assert.Equal("SC-8", rule.NistControl);
        Assert.Equal("32", rule.GdprArticle);
        Assert.False(rule.RequiredForCompliance);
        Assert.Equal(2.5, rule.ControlWeight);
        Assert.Equal(["internal-policy-17"], rule.GovernanceReferences);
        Assert.True(rule.MinScore < rule.MaxScore);
    }

    [Fact]
    public void CreateISO_uses_safe_defaults_for_optional_governance_collections()
    {
        var rule = SecurityRuleSetFactory.CreateISO(
            id: "TEST-ISO-002",
            title: "Minimal governed rule",
            desc: "Optional governance metadata omitted.",
            category: SecurityCategory.Application,
            severity: SecuritySeverity.Medium,
            risk: RiskLevel.Moderate,
            owasp: null,
            cwe: null,
            vuln: VulnerabilityType.SecurityMisconfiguration,
            tags: [],
            pattern: "unsafe",
            surface: "Application",
            remediation: "Apply a secure configuration.",
            reference: "https://example.invalid/minimal");

        Assert.NotNull(rule.AffectedArtifacts);
        Assert.Empty(rule.AffectedArtifacts);
        Assert.NotNull(rule.GovernanceReferences);
        Assert.Empty(rule.GovernanceReferences);
        Assert.True(rule.RequiredForCompliance);
        Assert.Equal(1.0, rule.ControlWeight);
    }
}
