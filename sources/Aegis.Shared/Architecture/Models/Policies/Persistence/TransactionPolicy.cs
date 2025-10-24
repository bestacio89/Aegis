namespace Aegis.Shared.Architecture.Models.Policies.Persistence
{
    /// <summary>
    /// Governs transaction and data access best practices across multiple ecosystems.
    /// </summary>
    public class TransactionPolicy
    {
        public bool Enabled { get; set; } = true;

        // Expected frameworks per language
        public Dictionary<string, string[]> FrameworkHints { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CSharp"] = new[] { "TransactionScope", "BeginTransaction", "DbContextTransaction" },
            ["Java"] = new[] { "@Transactional", "EntityManager.getTransaction()", "Connection.setAutoCommit(false)" },
            ["Python"] = new[] { "session.begin()", "with session:", "with transaction.atomic()" },
            ["Node"] = new[] { "sequelize.transaction", "mongoose.startSession" }
        };

        // Transaction misuse patterns
        public bool RequireExplicitCommit { get; set; } = true;
        public bool RequireRollbackOnFailure { get; set; } = true;
        public bool WarnReadOnlyTransactions { get; set; } = true;

        // Direct SQL concerns
        public bool DetectRawSqlConcatenation { get; set; } = true;
        public bool AllowParameterizedQueriesOnly { get; set; } = true;

        // Scope limits
        public int MaxTransactionBlockLines { get; set; } = 120;  // detect massive transaction scopes
        public bool DetectNestedTransactions { get; set; } = true;
    }
}
