using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// 🧩 Backend maintainability and quality ruleset.
/// Ensures cohesion, complexity, and error handling are within recommended thresholds.
/// </summary>
public static class BackendRuleset
{
    public static IEnumerable<ArchitectureRuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 🧠 Code Cohesion
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-BACK-R1",
            Name = "Cohesion Too Low",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "CohesionIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 75,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Increase cohesion by grouping related functionality and reducing class fragmentation."
        },

        // ==========================================================
        // ⚙️ Cyclomatic Complexity
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-BACK-R2",
            Name = "Complexity Exceeds Recommended Limit",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "CyclomaticComplexityIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 10,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Refactor long methods or introduce smaller helper functions."
        },

        // ==========================================================
        // 🔗 Dependency Graph
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-BACK-R3",
            Name = "Dependency Graph Too Dense",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "DependencyGraphDensity",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.6,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Reduce inter-module dependencies or extract shared interfaces."
        },

        // ==========================================================
        // 🚨 Error Handling
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-BACK-R4",
            Name = "Error Handling Incomplete",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "ErrorHandlingCoverage",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Add centralized exception handling and ensure try/catch coverage."
        },

        // ==========================================================
        // 🧮 Maintainability Index
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-BACK-R5",
            Name = "Maintainability Index Too Low",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "MaintainabilityIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 70,
            Severity = ArchitectureRuleSeverity.Critical,
            Recommendation = "Refactor for clarity, reduce file length, and improve comment density."
        }
    };
}
