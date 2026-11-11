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
public sealed class ArchitecturalTransactionEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly TransactionPolicy _policy;


    public override string Name =>
        nameof(ArchitecturalTransactionEvaluator);


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



    public ArchitecturalTransactionEvaluator(
        ILogger<ArchitecturalTransactionEvaluator> logger,
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



        var files = ResolveSourceFiles(
            projectPath,
            ".cs",
            ".java",
            ".py",
            ".ts",
            ".js");



        _logger.LogInformation(
            "💾 Running {Evaluator} on {Count} files",
            Name,
            files.Count);



        var analyzedFileResults = new List<ArchitectureEvaluatorResult>();
        var unreadableFileCount = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException exception)
            {
                unreadableFileCount++;
                _logger.LogWarning(
                    exception,
                    "{Evaluator} could not read {FilePath}.",
                    Name,
                    file);
                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                unreadableFileCount++;
                _logger.LogWarning(
                    exception,
                    "{Evaluator} has no access to {FilePath}.",
                    Name,
                    file);
                continue;
            }

            var fileResult = EvaluateFile(file, content);
            analyzedFileResults.Add(fileResult);
            results.Add(fileResult);
        }

        AddSummary(
            results,
            projectPath,
            files.Count,
            analyzedFileResults,
            unreadableFileCount);


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

                ["TransactionIntegrityScore"] =
                    integrity
            },


            Metadata =
            new Dictionary<string, string>
            {
                ["FilePath"] =
                    file,
                ["FileName"] =
                    Path.GetFileName(file),
                ["EvaluationScope"] =
                    FileScope,

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
        string projectPath,
        int discoveredFileCount,
        IReadOnlyCollection<ArchitectureEvaluatorResult> analyzedFileResults,
        int unreadableFileCount)
    {
        var analyzedCount = analyzedFileResults.Count;
        var analysisComplete = unreadableFileCount == 0
            && analyzedCount == discoveredFileCount;

        var integrity = analyzedCount > 0
            ? analyzedFileResults.Average(result =>
                result.Metrics.GetValueOrDefault("TransactionIntegrityScore"))
            : 0d;

        var safety = analyzedCount > 0
            ? analyzedFileResults.Average(result =>
                result.Metrics.GetValueOrDefault("SafetyScore"))
            : 0d;

        var summary = new ArchitectureEvaluatorResult(
            nameof(ArchitecturalTransactionEvaluator),
            projectPath)
        {
            Category = "PersistenceSummary",
            Metrics = new Dictionary<string, double>
            {
                ["DiscoveredFiles"] = discoveredFileCount,
                ["AnalyzedFiles"] = analyzedCount,
                ["UnreadableFiles"] = unreadableFileCount,
                ["AverageTransactionIntegrityIndex"] = integrity,
                ["DataConsistencyHealthIndex"] = integrity * 0.6 + safety * 0.4
            },
            Metadata = new Dictionary<string, string>
            {
                ["Evaluator"] = nameof(ArchitecturalTransactionEvaluator),
                ["EvaluationScope"] = ProjectScope,
                ["AnalysisComplete"] = analysisComplete.ToString().ToLowerInvariant(),
                ["AnalysisStatus"] = analysisComplete
                    ? "Completed"
                    : "Incomplete",
                ["MeasurementMethod"] =
                    "Arithmetic mean of file-level transaction integrity measurements across all discovered source files."
            }
        };

        if (analysisComplete && analyzedCount > 0)
        {
            // The project-level rule contract consumes this exact metric key.
            // Do not expose it when any discovered source file was unreadable.
            summary.Metrics["TransactionIntegrityIndex"] = integrity;

            var fileScores = analyzedFileResults
                .Select(result => result.Metrics["TransactionIntegrityScore"])
                .ToArray();

            summary.Metadata["Evidence.TransactionIntegrityIndex"] =
                $"Arithmetic mean of {analyzedCount} file-level transaction integrity measurements; " +
                $"observed range {fileScores.Min():F2}–{fileScores.Max():F2}. " +
                "Individual file measurements are emitted as separate evaluator facts.";
            summary.Metadata["EvidenceScope.TransactionIntegrityIndex"] =
                "ProjectAggregate";
        }
        else if (!analysisComplete)
        {
            summary.Metadata["IncompleteReason"] =
                $"{unreadableFileCount} unreadable file(s); {analyzedCount} of {discoveredFileCount} source file(s) analyzed.";
        }
        else
        {
            summary.Metadata["AnalysisStatus"] = "NoSourceFiles";
        }

        results.Add(summary);
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