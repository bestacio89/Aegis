using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

public static class InfrastructureRuleset
{
    public static IEnumerable<ArchitectureRuleDefinition> Get() => new[]
    {
        new ArchitectureRuleDefinition
        {
            Id = "AEG-INF-R1",
            Name = "Configuration Hygiene Low",
            Category = nameof(ArchitectureRuleCategory.Infrastructure),
            MetricKey = "ConfigurationHealthIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Critical,
            Recommendation = "Validate config files, remove secrets, and ensure YAML/JSON syntax correctness."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-INF-R2",
            Name = "Logging Hygiene Low",
            Category = nameof(ArchitectureRuleCategory.Infrastructure),
            MetricKey = "LoggingHygieneIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Use structured logging and avoid console or print statements."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-INF-R3",
            Name = "Repository Hygiene Low",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "RepositoryHealthIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Critical,
            Recommendation = "Ensure presence of governance, CI/CD and changelog files."
        }
    };
}
