using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class ArchitectureRuleDefinitionTests
{
    [Theory]
    [InlineData(ComparisonOperator.GreaterThan, 2.0, 1.0, true)]
    [InlineData(ComparisonOperator.GreaterThan, 1.0, 1.0, false)]
    [InlineData(ComparisonOperator.GreaterOrEqual, 1.0, 1.0, true)]
    [InlineData(ComparisonOperator.LessThan, 0.5, 1.0, true)]
    [InlineData(ComparisonOperator.LessOrEqual, 1.0, 1.0, true)]
    [InlineData(ComparisonOperator.Equal, 1.00005, 1.0, true)]
    [InlineData(ComparisonOperator.Equal, 1.001, 1.0, false)]
    [InlineData(ComparisonOperator.NotEqual, 1.001, 1.0, true)]
    [InlineData(ComparisonOperator.NotEqual, 1.00005, 1.0, false)]
    public void Evaluate_applies_the_declared_comparison_operator(
        ComparisonOperator comparison,
        double observed,
        double threshold,
        bool expected)
    {
        var rule = new ArchitectureRuleDefinition
        {
            Id = "TEST-RULE",
            MetricKey = "TestMetric",
            Operator = comparison,
            Threshold = threshold
        };

        Assert.Equal(expected, rule.Evaluate(observed));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Evaluate_rejects_non_finite_measurements(double observed)
    {
        var rule = new ArchitectureRuleDefinition
        {
            Id = "TEST-RULE",
            MetricKey = "TestMetric",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0
        };

        Assert.False(rule.Evaluate(observed));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Evaluate_rejects_non_finite_thresholds(double threshold)
    {
        var rule = new ArchitectureRuleDefinition
        {
            Id = "TEST-RULE",
            MetricKey = "TestMetric",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = threshold
        };

        Assert.False(rule.Evaluate(1));
    }

    [Fact]
    public void Policy_threshold_overrides_are_case_insensitive()
    {
        var policy = new AegisArchitecturePolicy();
        policy.RuleThresholds["AEG-ARCH-R1"] = 3;

        Assert.True(policy.RuleThresholds.TryGetValue("aeg-arch-r1", out var threshold));
        Assert.Equal(3, threshold);
    }
}
