using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Persistence;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Persistence;

/// <summary>
/// Evaluates transaction-related persistence hygiene:
/// - commit consistency
/// - rollback handling
/// - transaction scope maturity
/// - persistence safety signals
///
/// Produces:
/// - TransactionIntegrityIndex
/// - DataConsistencyHealthIndex
///
/// Framework agnostic.
/// </summary>
public sealed class TransactionEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly TransactionPolicy _policy;


    public override string Name =>
        nameof(TransactionEvaluator);


    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "TypeScript",
        "JavaScript"
    ];


    public override string[] SupportedFrameworks =>
    [
        "PersistenceAgnostic"
    ];



    private static readonly Regex CommitRegex =
        new(
            @"\b(commit|CommitAsync|CommitTransaction|save|flush)\b",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    private static readonly Regex RollbackRegex =
        new(
            @"\b(rollback|RollbackAsync|RollbackTransaction|abort)\b",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    private static readonly Regex TransactionRegex =
        new(
            @"\b(TransactionScope|BeginTransaction|transaction|Transactional|atomic)\b",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    private static readonly Regex RawSqlRegex =
        new(
            @"\b(select|insert|update|delete)\b[\s\S]{0,200}(\+|\$|\{)",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    public TransactionEvaluator(
        ILogger<TransactionEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Transaction ??
            new TransactionPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();


        if (!_policy.Enabled)
        {
            _logger.LogInformation(
                "⏭ {Evaluator} disabled by policy.",
                Name);

            return results;
        }



        var files =
            GetSourceFiles(projectPath);



        _logger.LogInformation(
            "💾 Running {Evaluator} on {Count} files",
            Name,
            files.Count);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            var content =
                await TryReadFileAsync(
                    file,
                    token);


            if (content is null)
                continue;



            results.Add(
                EvaluateFile(
                    file,
                    content));
        }



        if (results.Count > 0)
        {
            AddSummary(
                results,
                projectPath);
        }



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} results",
            Name,
            results.Count);


        return results;
    }





    private ArchitectureEvaluatorResult EvaluateFile(
        string file,
        string content)
    {
        var hasTransaction =
            TransactionRegex.IsMatch(content);



        var commitScore =
            EvaluateCommit(content, hasTransaction);



        var rollbackScore =
            EvaluateRollback(content, hasTransaction);



        var isolationScore =
            EvaluateIsolation(content, hasTransaction);



        var safetyScore =
            EvaluateSafety(content);



        var integrity =
            ComputeIntegrity(
                commitScore,
                rollbackScore,
                isolationScore,
                safetyScore);



        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            Category = "Persistence",


            Metrics =
            new Dictionary<string, double>
            {
                ["CommitScore"] =
                    commitScore * 100,

                ["RollbackScore"] =
                    rollbackScore * 100,

                ["IsolationScore"] =
                    isolationScore * 100,

                ["SafetyScore"] =
                    safetyScore * 100,

                ["TransactionIntegrityIndex"] =
                    integrity
            },


            Metadata =
            new Dictionary<string, string>
            {
                ["FilePath"] =
                    file,
                ["FileName"] =
                    Path.GetFileName(file),

                ["Language"] =
                    DetectLanguage(file),

                ["ContainsTransactionScope"] =
                    hasTransaction.ToString(),

                ["Evaluator"] =
                    Name
            }
        };
    }





    private double EvaluateCommit(
        string content,
        bool hasTransaction)
    {
        if (!hasTransaction ||
            !_policy.RequireExplicitCommit)
        {
            return 1;
        }


        return CommitRegex.IsMatch(content)
            ? 1
            : 0.6;
    }





    private double EvaluateRollback(
        string content,
        bool hasTransaction)
    {
        if (!hasTransaction ||
            !_policy.RequireRollbackOnFailure)
        {
            return 1;
        }


        return RollbackRegex.IsMatch(content)
            ? 1
            : 0.6;
    }





    private double EvaluateIsolation(
        string content,
        bool hasTransaction)
    {
        if (!hasTransaction ||
            _policy.MaxTransactionBlockLines <= 0)
        {
            return 1;
        }


        var lineCount =
            content.Split('\n').Length;


        return lineCount >
               _policy.MaxTransactionBlockLines
            ? 0.85
            : 1;
    }





    private double EvaluateSafety(
        string content)
    {
        if (!_policy.DetectRawSqlConcatenation)
            return 1;


        return RawSqlRegex.IsMatch(content)
            ? 0.6
            : 1;
    }





    private static void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var analyzedCount =
            results.Count;



        var integrity =
            results.Average(x =>
                x.Metrics.GetValueOrDefault(
                    "TransactionIntegrityIndex",
                    0));



        var safety =
            results.Average(x =>
                x.Metrics.GetValueOrDefault(
                    "SafetyScore",
                    0));



        results.Add(
            new ArchitectureEvaluatorResult(
                nameof(TransactionEvaluator),
                projectPath)
            {
                Category =
                    "PersistenceSummary",


                Metrics =
                new Dictionary<string, double>
                {
                    ["AnalyzedFiles"] =
                        analyzedCount,

                    ["AverageTransactionIntegrityIndex"] =
                        integrity,

                    ["DataConsistencyHealthIndex"] =
                        integrity * 0.6 +
                        safety * 0.4
                },


                Metadata =
                new Dictionary<string, string>
                {
                    ["Evaluator"] =
                        nameof(TransactionEvaluator)
                }
            });
    }





    private static double ComputeIntegrity(
        double commit,
        double rollback,
        double isolation,
        double safety)
    {
        return Math.Round(
            (
                commit * 0.35 +
                rollback * 0.30 +
                isolation * 0.20 +
                safety * 0.15
            ) * 100,
            2);
    }





    private static List<string> GetSourceFiles(
        string projectPath)
    {
        return Directory
            .EnumerateFiles(
                projectPath,
                "*.*",
                SearchOption.AllDirectories)
            .Where(IsSupportedFile)
            .Where(f => !IsExcludedDir(f))
            .ToList();
    }





    private static async Task<string?> TryReadFileAsync(
        string file,
        CancellationToken token)
    {
        try
        {
            return await File.ReadAllTextAsync(
                file,
                token);
        }
        catch
        {
            return null;
        }
    }





    private static bool IsSupportedFile(
        string file)
    {
        return
            file.EndsWith(".cs",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".java",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".py",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".ts",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".js",
                StringComparison.OrdinalIgnoreCase);
    }





    private static string DetectLanguage(
        string path)
    {
        if (path.EndsWith(".cs",
            StringComparison.OrdinalIgnoreCase))
            return "C#";


        if (path.EndsWith(".java",
            StringComparison.OrdinalIgnoreCase))
            return "Java";


        if (path.EndsWith(".py",
            StringComparison.OrdinalIgnoreCase))
            return "Python";


        if (path.EndsWith(".ts",
            StringComparison.OrdinalIgnoreCase))
            return "TypeScript";


        if (path.EndsWith(".js",
            StringComparison.OrdinalIgnoreCase))
            return "JavaScript";


        return "Unknown";
    }
}