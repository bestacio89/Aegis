using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// ⚡ Performance and efficiency rule set.
/// Ensures optimal runtime behavior, correct async usage, and prevents resource bottlenecks.
/// </summary>
public static class PerformanceRuleset
{
    public static IEnumerable<ArchitectureRuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 🧮 General Efficiency
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-PERF-R1",
            Scope = RuleEvaluationScope.File,
            Name = "Performance Efficiency Below Threshold",
            Category = nameof(ArchitectureRuleCategory.Performance),
            MetricKey = "PerformanceHealthIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Optimize nested loops, prefer data projections, and eliminate redundant computations. " +
                             "Use profiling tools to identify hot paths and minimize blocking operations."
        },

        // ==========================================================
        // 🧵 Async & Threading Behavior
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-PERF-R2",
            Scope = RuleEvaluationScope.File,
            Name = "Async Maturity Below Threshold",
            Category = nameof(ArchitectureRuleCategory.Performance),
            MetricKey = "AsyncMaturityScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Avoid Thread.Sleep(), Task.Wait(), or .Result on async operations. " +
                             "Use awaitable patterns and proper cancellation tokens to maintain responsiveness."
        },

        // ==========================================================
        // 🧠 Optional Future Metrics (for AI integration or future detectors)
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-PERF-R3",
            Scope = RuleEvaluationScope.Project,
            Name = "Memory Allocation Excessive",
            Category = nameof(ArchitectureRuleCategory.Performance),
            MetricKey = "MemoryAllocationRate",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.7,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Reduce heap allocations and prefer object pooling or stack-based structures where possible."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-PERF-R4",
            Scope = RuleEvaluationScope.File,
            Name = "Blocking I/O Detected",
            Category = nameof(ArchitectureRuleCategory.Performance),
            MetricKey = "BlockingIOCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Avoid blocking I/O on UI or API threads. Replace with async I/O or background processing patterns."
        }
    };
}
