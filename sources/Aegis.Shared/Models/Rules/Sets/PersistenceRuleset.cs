using Aegis.Shared.Enums;

namespace Aegis.Shared.Rules.Sets.Persistence;

public static class PersistenceRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        new RuleDefinition
        {
            Id = "AEG-TXN-R1",
            Name = "Transaction Integrity Low",
            Category = nameof(RuleCategory.Persistence),
            MetricKey = "TransactionIntegrityIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Warning,
            Recommendation = "Ensure proper commit/rollback and avoid raw SQL concatenation."
        },
        new RuleDefinition
        {
            Id = "AEG-TXN-R2",
            Name = "Data Access Hygiene Violation",
            Category = nameof(RuleCategory.Persistence),
            MetricKey = "DataAccessHygieneIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = RuleSeverity.Warning,
            Recommendation = "Ensure ORM patterns are followed and transactions are scoped properly."
        }
    };
}
