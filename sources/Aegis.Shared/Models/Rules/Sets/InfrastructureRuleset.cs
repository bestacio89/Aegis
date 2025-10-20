using Aegis.Shared.Enums;

namespace Aegis.Shared.Rules.Sets.Infrastructure;

public static class InfrastructureRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        new RuleDefinition
        {
            Id = "AEG-INF-R1",
            Name = "Configuration Hygiene Low",
            Category = nameof(RuleCategory.Infrastructure),
            MetricKey = "ConfigurationHealthIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Critical,
            Recommendation = "Validate config files, remove secrets, and ensure YAML/JSON syntax correctness."
        },
        new RuleDefinition
        {
            Id = "AEG-INF-R2",
            Name = "Logging Hygiene Low",
            Category = nameof(RuleCategory.Infrastructure),
            MetricKey = "LoggingHygieneIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = RuleSeverity.Medium,
            Recommendation = "Use structured logging and avoid console or print statements."
        },
        new RuleDefinition
        {
            Id = "AEG-INF-R3",
            Name = "Repository Hygiene Low",
            Category = nameof(RuleCategory.Maintainability),
            MetricKey = "RepositoryHealthIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Critical,
            Recommendation = "Ensure presence of governance, CI/CD and changelog files."
        }
    };
}
