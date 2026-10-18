using Aegis.Shared.Security.Models.Rules.Sets;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class SecurityRuleSetCatalogTests
{
    public static IEnumerable<object[]> RuleSets()
    {
        yield return [new ApplicationRuleSet()];
        yield return [new ComplianceRuleSet()];
        yield return [new DependencyRuleSet()];
        yield return [new GovernanceRuleSet()];
        yield return [new IaCRuleSet()];
        yield return [new ISO27001RuleSet()];
        yield return [new InfrastructureRuleSet()];
        yield return [new NetworkRuleSet()];
        yield return [new SecretsRuleSet()];
    }

    [Theory]
    [MemberData(nameof(RuleSets))]
    public void Rule_set_has_identity_and_at_least_one_rule(SecurityRuleSet ruleSet)
    {
        Assert.False(string.IsNullOrWhiteSpace(ruleSet.Key));
        Assert.False(string.IsNullOrWhiteSpace(ruleSet.Name));
        Assert.False(string.IsNullOrWhiteSpace(ruleSet.Description));
        Assert.NotEmpty(ruleSet.Rules);
    }

    [Theory]
    [MemberData(nameof(RuleSets))]
    public void Rule_ids_are_non_empty_and_unique_within_each_set(SecurityRuleSet ruleSet)
    {
        var ids = ruleSet.Rules.Select(rule => rule.RuleId).ToArray();

        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [MemberData(nameof(RuleSets))]
    public void Rule_definitions_have_descriptive_metadata_and_finite_score_ranges(SecurityRuleSet ruleSet)
    {
        foreach (var rule in ruleSet.Rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Title));
            Assert.False(string.IsNullOrWhiteSpace(rule.Description));
            Assert.False(string.IsNullOrWhiteSpace(rule.Remediation));
            Assert.False(string.IsNullOrWhiteSpace(rule.ReferenceUrl));
            Assert.True(double.IsFinite(rule.MinScore), $"{rule.RuleId} has a non-finite minimum score.");
            Assert.True(double.IsFinite(rule.MaxScore), $"{rule.RuleId} has a non-finite maximum score.");
            Assert.True(
                rule.MinScore <= rule.MaxScore,
                $"{rule.RuleId} has an inverted score range ({rule.MinScore} > {rule.MaxScore}).");
        }
    }
}
