using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Enums;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// 💾 Persistence and transactional consistency rule set.
/// Ensures correct data access, transaction safety, and ORM best practices.
/// </summary>
public static class PersistenceRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 🔁 Transaction Safety
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-TXN-R1",
            Name = "Transaction Integrity Low",
            Category = nameof(RuleCategory.Persistence),
            MetricKey = "TransactionIntegrityIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.High,
            Recommendation = "Ensure every transactional operation has proper commit/rollback control. " +
                             "Avoid manual SQL concatenation and always use parameterized queries or ORM transactions."
        },

        // ==========================================================
        // 🧩 ORM & Repository Hygiene
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-TXN-R2",
            Name = "Data Access Hygiene Violation",
            Category = nameof(RuleCategory.Persistence),
            MetricKey = "DataAccessHygieneIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = RuleSeverity.High,
            Recommendation = "Follow ORM and repository best practices — respect unit of work boundaries, " +
                             "avoid direct context manipulation outside of repositories, and ensure transactions are properly scoped."
        },

        // ==========================================================
        // ⚙️ Connection Management
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-TXN-R3",
            Name = "Connection Pool Misuse",
            Category = nameof(RuleCategory.Persistence),
            MetricKey = "ConnectionPoolAbuseCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Medium,
            Recommendation = "Do not open persistent database connections manually. Let the ORM or data provider handle connection pooling."
        },

        // ==========================================================
        // 🔐 Data Consistency
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-TXN-R4",
            Name = "Uncommitted Data Detected",
            Category = nameof(RuleCategory.Persistence),
            MetricKey = "UncommittedTransactionCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Critical,
            Recommendation = "Detected open or incomplete transactions. Ensure all transactions are properly committed or rolled back."
        }
    };
}
