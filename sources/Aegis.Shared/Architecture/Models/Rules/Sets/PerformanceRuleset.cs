using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Enums;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// ⚡ Performance and efficiency rule set.
/// Ensures optimal runtime behavior, correct async usage, and prevents resource bottlenecks.
/// </summary>
public static class PerformanceRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 🧮 General Efficiency
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-PERF-R1",
            Name = "Performance Efficiency Below Threshold",
            Category = nameof(RuleCategory.Performance),
            MetricKey = "PerformanceHealthIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = RuleSeverity.High,
            Recommendation = "Optimize nested loops, prefer data projections, and eliminate redundant computations. " +
                             "Use profiling tools to identify hot paths and minimize blocking operations."
        },

        // ==========================================================
        // 🧵 Async & Threading Behavior
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-PERF-R2",
            Name = "Threading or Async Misuse",
            Category = nameof(RuleCategory.Performance),
            MetricKey = "AsyncUsageCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.High,
            Recommendation = "Avoid Thread.Sleep(), Task.Wait(), or .Result on async operations. " +
                             "Use awaitable patterns and proper cancellation tokens to maintain responsiveness."
        },

        // ==========================================================
        // 🧠 Optional Future Metrics (for AI integration or future detectors)
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-PERF-R3",
            Name = "Memory Allocation Excessive",
            Category = nameof(RuleCategory.Performance),
            MetricKey = "MemoryAllocationRate",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.7,
            Severity = RuleSeverity.Medium,
            Recommendation = "Reduce heap allocations and prefer object pooling or stack-based structures where possible."
        },
        new RuleDefinition
        {
            Id = "AEG-PERF-R4",
            Name = "Blocking I/O Detected",
            Category = nameof(RuleCategory.Performance),
            MetricKey = "BlockingIOCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.High,
            Recommendation = "Avoid blocking I/O on UI or API threads. Replace with async I/O or background processing patterns."
        }
    };
}
