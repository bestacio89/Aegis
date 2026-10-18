using Aegis.Architecture.Scoring;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class RuleWeightingEngineTests
{
    [Theory]
    [InlineData(ArchitectureRuleSeverity.Critical, 1.5)]
    [InlineData(ArchitectureRuleSeverity.High, 1.25)]
    [InlineData(ArchitectureRuleSeverity.Medium, 1.0)]
    [InlineData(ArchitectureRuleSeverity.Low, 0.75)]
    [InlineData(ArchitectureRuleSeverity.Info, 0.5)]
    public void WeightOf_uses_the_configured_base_weight(
        ArchitectureRuleSeverity severity,
        double expected)
    {
        var engine = CreateEngine();

        Assert.Equal(expected, engine.WeightOf(severity, "Architecture"));
    }

    [Fact]
    public void ApplyWeights_sets_violation_impact_and_zeroes_compliant_impact()
    {
        var engine = CreateEngine();
        var violation = CreateResult(ArchitectureRuleSeverity.High, isCompliant: false);
        var compliant = CreateResult(ArchitectureRuleSeverity.High, isCompliant: true);

        var results = engine.ApplyWeights([violation, compliant]).ToArray();

        Assert.Equal(2, results.Length);
        Assert.Equal(1.25, violation.WeightedImpact);
        Assert.Equal(0, compliant.WeightedImpact);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 10, 1)]
    [InlineData(5, 10, 0.5)]
    [InlineData(10, 10, 0)]
    [InlineData(20, 10, 0)]
    public void ComputeScore_normalizes_compliance_and_clamps_to_unit_interval(
        double violationWeight,
        double checkWeight,
        double expected)
    {
        Assert.Equal(expected, CreateEngine().ComputeScore(violationWeight, checkWeight));
    }

    [Fact]
    public void ComputeWeightedCompliance_returns_one_for_empty_input()
    {
        Assert.Equal(1, CreateEngine().ComputeWeightedCompliance([]));
    }

    private static RuleWeightingEngine CreateEngine() =>
        new(NullLogger<RuleWeightingEngine>.Instance);

    private static ArchitectureRuleresult CreateResult(
        ArchitectureRuleSeverity severity,
        bool isCompliant) =>
        new(
            "TEST-RULE",
            "Test rule",
            ArchitectureRuleCategory.General,
            severity,
            "Widget.cs",
            null,
            "Test finding",
            DateTimeOffset.UtcNow,
            isCompliant);
}
