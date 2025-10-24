using System.Text.RegularExpressions;
using Aegis.Core.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Persistence;
using Aegis.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Core.Architecture.Evaluators.Persistence;

/// <summary>
/// Evaluates transaction hygiene, consistency, and data integrity across multiple languages.
/// Produces quantitative metrics (CommitScore, RollbackScore, IsolationScore, SafetyScore)
/// aggregated into a TransactionIntegrityIndex.
/// </summary>
public sealed class TransactionEvaluator : BaseArchitectureEvaluator
{
    private readonly TransactionPolicy _policy;

    public override string Name => "TransactionEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Java", "Python", "TypeScript", "JavaScript"];
    public override string[] SupportedFrameworks => ["EFCore", "Spring", "SQLAlchemy", "Mongoose", "Sequelize"];

    private static readonly Regex CommitRx = new(@"\b(commit|CommitTransaction|save|session\.commit|connection\.commit)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RollbackRx = new(@"\b(rollback|RollbackTransaction|session\.rollback|connection\.rollback)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RawSqlConcatRx = new(@"\b(select|insert|update|delete)\b.+(\+|\$|\{)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TransactionHintRx = new(@"\b(BeginTransaction|TransactionScope|@Transactional|with\s+transaction|sequelize\.transaction|mongoose\.startSession)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public TransactionEvaluator(ILogger<TransactionEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Transaction ?? new TransactionPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.Enabled)
        {
            _logger.LogInformation("⏭ {Evaluator} disabled by policy.", Name);
            return results;
        }

        _logger.LogInformation("💾 Running {Evaluator} on {Path}", Name, projectPath);

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".java") || f.EndsWith(".py") || f.EndsWith(".ts") || f.EndsWith(".js"))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;
            try { content = await File.ReadAllTextAsync(file, token); }
            catch { continue; }

            var lang = DetectLanguage(file);
            if (!TransactionHintRx.IsMatch(content))
                continue; // Skip non-transactional files

            // Initialize sub-scores (0–1 scale)
            double commitScore = 1.0;
            double rollbackScore = 1.0;
            double isolationScore = 1.0;
            double safetyScore = 1.0;

            // 🧾 Commit handling
            if (_policy.RequireExplicitCommit && !CommitRx.IsMatch(content))
                commitScore -= 0.4;

            // 🔁 Rollback handling
            if (_policy.RequireRollbackOnFailure && !RollbackRx.IsMatch(content))
                rollbackScore -= 0.4;

            // 🧩 Nested transaction detection
            var txCount = _policy.FrameworkHints.TryGetValue(lang, out var hints)
                ? hints.Count(h => content.Contains(h, StringComparison.OrdinalIgnoreCase))
                : 0;

            if (_policy.DetectNestedTransactions && txCount > 1)
                isolationScore -= 0.2;

            // 📏 Oversized transaction block
            var totalLines = content.Split('\n').Length;
            if (totalLines > _policy.MaxTransactionBlockLines)
                isolationScore -= 0.1;

            // ⚠️ Raw SQL concatenation
            if (_policy.DetectRawSqlConcatenation && RawSqlConcatRx.IsMatch(content))
                safetyScore -= 0.4;

            // 🧩 Read-only marker (neutral informational case)
            if (_policy.WarnReadOnlyTransactions && content.Contains("ReadOnly", StringComparison.OrdinalIgnoreCase))
                safetyScore -= 0.05;

            // 🧮 Compute aggregate Transaction Integrity Index
            double transactionHealth = ComputeTransactionIntegrity(commitScore, rollbackScore, isolationScore, safetyScore);

            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Persistence",
                Metrics = new Dictionary<string, double>
                {
                    ["CommitScore"] = Math.Max(0, commitScore),
                    ["RollbackScore"] = Math.Max(0, rollbackScore),
                    ["IsolationScore"] = Math.Max(0, isolationScore),
                    ["SafetyScore"] = Math.Max(0, safetyScore),
                    ["TransactionIntegrityIndex"] = transactionHealth
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Language"] = lang,
                    ["Frameworks"] = string.Join(", ", _policy.FrameworkHints.GetValueOrDefault(lang) ?? Array.Empty<string>()),
                    ["PolicyEnabled"] = _policy.Enabled.ToString(),
                    ["FileName"] = Path.GetFileName(file)
                }
            });
        }

        // 📊 Aggregate repository-level summary
        if (results.Count > 0)
        {
            double avgIndex = results.Average(r => r.Metrics.GetValueOrDefault("TransactionIntegrityIndex", 0));
            double avgCommit = results.Average(r => r.Metrics.GetValueOrDefault("CommitScore", 0));
            double avgRollback = results.Average(r => r.Metrics.GetValueOrDefault("RollbackScore", 0));
            double avgSafety = results.Average(r => r.Metrics.GetValueOrDefault("SafetyScore", 0));

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "PersistenceSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["AnalyzedFiles"] = results.Count,
                    ["AverageTransactionIntegrityIndex"] = avgIndex,
                    ["AverageCommitScore"] = avgCommit,
                    ["AverageRollbackScore"] = avgRollback,
                    ["AverageSafetyScore"] = avgSafety,
                    ["DataConsistencyHealthIndex"] = avgIndex * 0.6 + avgSafety * 0.4
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.Enabled.ToString(),
                    ["SupportedFrameworks"] = string.Join(", ", SupportedFrameworks)
                }
            });
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} results", Name, results.Count);
        return results;
    }

    private static double ComputeTransactionIntegrity(double commit, double rollback, double isolation, double safety)
    {
        // Weighted composite emphasizing commit/rollback reliability
        double score = commit * 0.35 + rollback * 0.3 + isolation * 0.2 + safety * 0.15;
        return Math.Round(score * 100, 2);
    }

    private static string DetectLanguage(string path)
    {
        if (path.EndsWith(".cs")) return "CSharp";
        if (path.EndsWith(".java")) return "Java";
        if (path.EndsWith(".py")) return "Python";
        if (path.EndsWith(".ts") || path.EndsWith(".js")) return "Node";
        return "Unknown";
    }
}
