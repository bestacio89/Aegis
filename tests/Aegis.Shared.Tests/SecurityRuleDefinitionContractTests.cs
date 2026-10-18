using System.Text.Json;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models.Rules;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class SecurityRuleDefinitionContractTests
{
    [Fact]
    public void New_definition_has_safe_collection_defaults()
    {
        var definition = new SecurityRuleDefinition();

        Assert.NotNull(definition.AffectedArtifacts);
        Assert.Empty(definition.AffectedArtifacts);
        Assert.NotNull(definition.Tags);
        Assert.Empty(definition.Tags);
        Assert.Null(definition.IsoControl);
        Assert.Null(definition.IsoClause);
        Assert.Null(definition.GdprArticle);
        Assert.Null(definition.NistControl);
    }

    [Fact]
    public void Definition_preserves_security_and_governance_fields_through_json_round_trip()
    {
        var definition = new SecurityRuleDefinition
        {
            RuleId = "AEG-SEC-CONTRACT-001",
            Title = "Require encrypted transport",
            Description = "Sensitive traffic must use an approved encrypted transport.",
            Category = SecurityCategory.Network,
            Severity = SecuritySeverity.High,
            MinScore = 7.0,
            MaxScore = 10.0,
            ThresholdType = "CVSS",
            AttackSurface = "Network",
            ImpactScope = "Remote",
            OwaspId = "A02:2021",
            CweId = "CWE-319",
            DetectionVector = "Static",
            EvidencePattern = "http://",
            AffectedArtifacts = ["appsettings.json", "Program.cs"],
            Remediation = "Use an approved TLS configuration.",
            ReferenceUrl = "https://example.invalid/security-transport",
            Tags = ["transport", "encryption"],
            IsoControl = "A.8.24",
            IsoClause = "Clause 8",
            GdprArticle = "32",
            NistControl = "SC-8"
        };

        var json = JsonSerializer.Serialize(definition);
        var restored = JsonSerializer.Deserialize<SecurityRuleDefinition>(json);

        Assert.NotNull(restored);
        Assert.Equal(definition.RuleId, restored.RuleId);
        Assert.Equal(definition.Title, restored.Title);
        Assert.Equal(definition.Category, restored.Category);
        Assert.Equal(definition.Severity, restored.Severity);
        Assert.Equal(definition.MinScore, restored.MinScore);
        Assert.Equal(definition.MaxScore, restored.MaxScore);
        Assert.Equal(definition.OwaspId, restored.OwaspId);
        Assert.Equal(definition.CweId, restored.CweId);
        Assert.Equal(definition.IsoControl, restored.IsoControl);
        Assert.Equal(definition.IsoClause, restored.IsoClause);
        Assert.Equal(definition.GdprArticle, restored.GdprArticle);
        Assert.Equal(definition.NistControl, restored.NistControl);
        Assert.Equal(definition.AffectedArtifacts, restored.AffectedArtifacts);
        Assert.Equal(definition.Tags, restored.Tags);
    }
}
