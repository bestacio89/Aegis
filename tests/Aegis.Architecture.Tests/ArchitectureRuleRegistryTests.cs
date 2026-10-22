using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureRuleRegistryTests
{
    [Fact]
    public void Production_rule_inventory_passes_all_registry_validation()
    {
        var rules = ArchitectureRuleRegistry.All;

        Assert.NotEmpty(rules);
        Assert.Empty(ArchitectureRuleRegistry.Validate(rules));
    }

    [Fact]
    public void Every_rule_has_a_unique_id_explicit_scope_and_declared_metric()
    {
        var rules = ArchitectureRuleRegistry.All;

        Assert.Equal(
            rules.Count,
            rules.Select(rule => rule.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        Assert.All(rules, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.MetricKey));
            Assert.Contains(
                rule.Scope,
                new[] { RuleEvaluationScope.File, RuleEvaluationScope.Project });
            Assert.True(double.IsFinite(rule.Threshold));
        });
    }

    [Fact]
    public void Facade_ratio_rule_uses_the_producers_ratio_scale_and_direction()
    {
        var rule = ArchitectureRuleRegistry.GetById("AEG-DES-FAC002");

        Assert.NotNull(rule);
        Assert.Equal("PublicSurfaceRatio", rule.MetricKey);
        Assert.Equal(ComparisonOperator.GreaterThan, rule.Operator);
        Assert.Equal(1.0, rule.Threshold);
    }

    [Fact]
    public void Constant_naming_has_its_own_registered_rule()
    {
        var rule = ArchitectureRuleRegistry.GetById("AEG-NAM-R7");

        Assert.NotNull(rule);
        Assert.Equal("ConstantNamingCompliance", rule.MetricKey);
        Assert.Equal(RuleEvaluationScope.File, rule.Scope);
    }

    [Theory]
    [InlineData("AEG-ARCH-R1", "aeg-arch-r1")]
    [InlineData("AEG-NAM-R1", "aeg-nam-r1")]
    [InlineData("AEG-DEPS-R1", "aeg-deps-r1")]
    public void GetById_is_case_insensitive(string declaredId, string lookupId)
    {
        var declared = ArchitectureRuleRegistry.GetById(declaredId);
        var lookedUp = ArchitectureRuleRegistry.GetById(lookupId);

        Assert.NotNull(declared);
        Assert.Same(declared, lookedUp);
    }

    [Fact]
    public void GetByCategory_returns_only_rules_in_the_requested_category()
    {
        var rules = ArchitectureRuleRegistry.GetByCategory(
            ArchitectureRuleCategory.Naming).ToArray();

        Assert.NotEmpty(rules);
        Assert.All(rules, rule =>
            Assert.Equal("Naming", rule.Category, ignoreCase: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void GetById_rejects_blank_identifiers(string? id)
    {
        Assert.ThrowsAny<ArgumentException>(() => ArchitectureRuleRegistry.GetById(id!));
    }
}
