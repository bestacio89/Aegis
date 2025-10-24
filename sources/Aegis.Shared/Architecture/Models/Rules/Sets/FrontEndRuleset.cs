using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Enums;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// 🎨 Frontend maintainability and best practices ruleset.
/// Enforces component structure, hook correctness, and naming conventions across major frontend frameworks.
/// </summary>
public static class FrontendRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 🅰️ Angular
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-FRONT-R1",
            Name = "Angular Selector Naming Violation",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "AngularSelectorCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = RuleSeverity.Medium,
            Recommendation = "Ensure selectors use approved prefixes and consistent casing (e.g., app-, core-, shared-)."
        },

        // ==========================================================
        // ⚛️ React
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-FRONT-R2",
            Name = "React Hook Misuse",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "ReactHookUsageIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.High,
            Recommendation = "Avoid calling hooks inside loops or conditionals. Respect React’s Hook rules and lifecycle order."
        },

        // ==========================================================
        // 🧩 General Frontend Architecture
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-FRONT-R3",
            Name = "Component Complexity Too High",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "ComponentComplexityIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 50,
            Severity = RuleSeverity.Low,
            Recommendation = "Split large components into smaller, reusable ones. Keep template logic minimal."
        }
    };
}
