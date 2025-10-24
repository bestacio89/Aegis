using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// 🎨 Frontend maintainability and best practices ruleset.
/// Enforces component structure, hook correctness, and naming conventions across major frontend frameworks.
/// </summary>
public static class FrontendRuleset
{
    public static IEnumerable<ArchitectureRuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 🅰️ Angular
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R1",
            Name = "Angular Selector Naming Violation",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "AngularSelectorCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure selectors use approved prefixes and consistent casing (e.g., app-, core-, shared-)."
        },

        // ==========================================================
        // ⚛️ React
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R2",
            Name = "React Hook Misuse",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "ReactHookUsageIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Avoid calling hooks inside loops or conditionals. Respect React’s Hook rules and lifecycle order."
        },

        // ==========================================================
        // 🧩 General Frontend Architecture
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R3",
            Name = "Component Complexity Too High",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "ComponentComplexityIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 50,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Split large components into smaller, reusable ones. Keep template logic minimal."
        }
    };
}
