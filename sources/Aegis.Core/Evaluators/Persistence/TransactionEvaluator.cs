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
/// Framework agnostic.
/// Produces:
/// - TransactionIntegrityIndex
/// - DataConsistencyHealthIndex
/// </summary>
public sealed class TransactionEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly TransactionPolicy _policy;


    public override string Name =>
        "TransactionEvaluator";


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





    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();


        if (!_policy.Enabled)
        {
            _logger.LogInformation(
                "⏭ {Evaluator} disabled by policy.",
                Name);

            return results;
        }



        var files =
            Directory
                .EnumerateFiles(
                    projectPath,
                    "*.*",
                    SearchOption.AllDirectories)
                .Where(IsSupportedFile)
                .Where(f => !IsExcludedDir(f))
                .ToList();



        _logger.LogInformation(
            "💾 Running {Evaluator} on {Count} files",
            Name,
            files.Count);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            string content;

            try
            {
                content =
                    await File.ReadAllTextAsync(
                        file,
                        token);
            }
            catch
            {
                continue;
            }



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
        double commit = 1;
        double rollback = 1;
        double isolation = 1;
        double safety = 1;



        bool hasTransaction =
            TransactionRegex.IsMatch(content);



        if (hasTransaction)
        {
            if (_policy.RequireExplicitCommit &&
                !CommitRegex.IsMatch(content))
            {
                commit -= 0.4;
            }



            if (_policy.RequireRollbackOnFailure &&
                !RollbackRegex.IsMatch(content))
            {
                rollback -= 0.4;
            }



            if (_policy.MaxTransactionBlockLines > 0)
            {
                var lines =
                    content.Split('\n').Length;


                if (lines >
                    _policy.MaxTransactionBlockLines)
                {
                    isolation -= 0.15;
                }
            }
        }



        if (_policy.DetectRawSqlConcatenation &&
            RawSqlRegex.IsMatch(content))
        {
            safety -= 0.4;
        }



        commit = Normalize(commit);
        rollback = Normalize(rollback);
        isolation = Normalize(isolation);
        safety = Normalize(safety);



        var integrity =
            ComputeIntegrity(
                commit,
                rollback,
                isolation,
                safety);



        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            Category =
                "Persistence",


            Metrics =
            new Dictionary<string, double>
            {
                ["CommitScore"] =
                    commit * 100,

                ["RollbackScore"] =
                    rollback * 100,

                ["IsolationScore"] =
                    isolation * 100,

                ["SafetyScore"] =
                    safety * 100,

                ["TransactionIntegrityIndex"] =
                    integrity
            },


            Metadata =
            new Dictionary<string, string>
            {
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





    private static void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var index =
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
                "TransactionEvaluator",
                projectPath)
            {
                Category =
                    "PersistenceSummary",


                Metrics =
                new Dictionary<string, double>
                {
                    ["AnalyzedFiles"] =
                        results.Count,

                    ["AverageTransactionIntegrityIndex"] =
                        index,

                    ["DataConsistencyHealthIndex"] =
                        index * 0.6 +
                        safety * 0.4
                },


                Metadata =
                new Dictionary<string, string>
                {
                    ["Evaluator"] =
                        "TransactionEvaluator"
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





    private static double Normalize(double value)
        => Math.Max(
            0,
            Math.Min(
                1,
                value));





    private static bool IsSupportedFile(string file)
    {
        return
            file.EndsWith(".cs",
                StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".java",
                StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".py",
                StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".ts",
                StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".js",
                StringComparison.OrdinalIgnoreCase);
    }





    private static string DetectLanguage(string path)
    {
        if (path.EndsWith(".cs"))
            return "C#";

        if (path.EndsWith(".java"))
            return "Java";

        if (path.EndsWith(".py"))
            return "Python";

        if (path.EndsWith(".ts"))
            return "TypeScript";

        if (path.EndsWith(".js"))
            return "JavaScript";

        return "Unknown";
    }
}