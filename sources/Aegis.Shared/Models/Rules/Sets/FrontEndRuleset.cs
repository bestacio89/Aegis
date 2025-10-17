using Aegis.Shared.Enums;

namespace Aegis.Shared.Rules.Sets.FrontEnd;

public static class FrontendRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        new RuleDefinition
        {
            Id = "AEG-FRONT-R1",
            Name = "Angular Selector Naming Violation",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "AngularSelectorCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = RuleSeverity.Warning,
            Recommendation = "Ensure selectors use approved prefixes and consistent casing."
        },
        new RuleDefinition
        {
            Id = "AEG-FRONT-R2",
            Name = "React Hook Misuse",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "ReactHookUsageIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Warning,
            Recommendation = "Avoid calling hooks in conditionals or loops. Respect React’s hook rules."
        },
        new RuleDefinition
        {
            Id = "AEG-FRONT-R3",
            Name = "Component Complexity Too High",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "ComponentComplexityIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 50,
            Severity = RuleSeverity.Info,
            Recommendation = "Split large components or templates into smaller, focused units."
        }
    };
}
