using Aegis.Shared.Enums;

namespace Aegis.Shared.Rules.Sets.BackEnd;

public static class BackendRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        new RuleDefinition
        {
            Id = "AEG-BACK-R1",
            Name = "Cohesion Too Low",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "CohesionIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 75,
            Severity = RuleSeverity.Warning,
            Recommendation = "Increase cohesion by grouping related functionality and reducing class fragmentation."
        },
        new RuleDefinition
        {
            Id = "AEG-BACK-R2",
            Name = "Complexity Exceeds Recommended Limit",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "CyclomaticComplexityIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 10,
            Severity = RuleSeverity.Warning,
            Recommendation = "Refactor long methods or introduce smaller helper functions."
        },
        new RuleDefinition
        {
            Id = "AEG-BACK-R3",
            Name = "Dependency Graph Too Dense",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "DependencyGraphDensity",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.6,
            Severity = RuleSeverity.Warning,
            Recommendation = "Reduce inter-module dependencies or extract shared interfaces."
        },
        new RuleDefinition
        {
            Id = "AEG-BACK-R4",
            Name = "Error Handling Incomplete",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "ErrorHandlingCoverage",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Warning,
            Recommendation = "Add centralized exception handling and ensure try/catch coverage."
        },
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
