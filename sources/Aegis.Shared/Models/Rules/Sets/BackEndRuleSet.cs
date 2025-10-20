using Aegis.Shared.Enums;

namespace Aegis.Shared.Rules.Sets.BackEnd;

/// <summary>
/// 🧩 Backend maintainability and quality ruleset.
/// Ensures cohesion, complexity, and error handling are within recommended thresholds.
/// </summary>
public static class BackendRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 🧠 Code Cohesion
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-BACK-R1",
            Name = "Cohesion Too Low",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "CohesionIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 75,
            Severity = RuleSeverity.Medium,
            Recommendation = "Increase cohesion by grouping related functionality and reducing class fragmentation."
        },

        // ==========================================================
        // ⚙️ Cyclomatic Complexity
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-BACK-R2",
            Name = "Complexity Exceeds Recommended Limit",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "CyclomaticComplexityIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 10,
            Severity = RuleSeverity.Medium,
            Recommendation = "Refactor long methods or introduce smaller helper functions."
        },

        // ==========================================================
        // 🔗 Dependency Graph
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-BACK-R3",
            Name = "Dependency Graph Too Dense",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "DependencyGraphDensity",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.6,
            Severity = RuleSeverity.Medium,
            Recommendation = "Reduce inter-module dependencies or extract shared interfaces."
        },

        // ==========================================================
        // 🚨 Error Handling
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-BACK-R4",
            Name = "Error Handling Incomplete",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "ErrorHandlingCoverage",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.High,
            Recommendation = "Add centralized exception handling and ensure try/catch coverage."
        },

        // ==========================================================
        // 🧮 Maintainability Index
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-BACK-R5",
            Name = "Maintainability Index Too Low",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "MaintainabilityIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 70,
            Severity = RuleSeverity.Critical,
            Recommendation = "Refactor for clarity, reduce file length, and improve comment density."
        }
    };
}
