using Aegis.Shared.Enums;

namespace Aegis.Shared.Rules.Sets.Performance;

public static class PerformanceRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        new RuleDefinition
        {
            Id = "AEG-PERF-R1",
            Name = "Performance Efficiency Below Threshold",
            Category = nameof(RuleCategory.Performance),
            MetricKey = "PerformanceHealthIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = RuleSeverity.Warning,
            Recommendation = "Optimize nested loops and avoid blocking calls."
        },
        new RuleDefinition
        {
            Id = "AEG-PERF-R2",
            Name = "Threading or Async Misuse",
            Category = nameof(RuleCategory.Performance),
            MetricKey = "AsyncUsageCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Warning,
            Recommendation = "Replace Thread.Sleep or .Result with awaitable async calls."
        }
    };
}
