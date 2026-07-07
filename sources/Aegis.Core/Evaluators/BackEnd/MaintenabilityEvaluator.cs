using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.BackEnd;

/// <summary>
/// Measures maintainability across source files, computing metrics such as
/// cyclomatic complexity, comment density, and maintainability index.
/// Emits EvaluatorResults consumed by the Rule Engine for governance.
/// </summary>
public sealed class MaintainabilityEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly MaintainabilityPolicy _policy;

    public override string Name => "MaintainabilityEvaluator";

    public override string[] SupportedLanguages => ["C#", "Java", "Python"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring Boot", "FastAPI"];

    public MaintainabilityEvaluator(
        ILogger<MaintainabilityEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Maintainability ?? new MaintainabilityPolicy();
    }

    /// <summary>
    /// Scans all source files to compute maintainability metrics and produces EvaluatorResults.
    /// </summary>
    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        // Filter by language
        var extensions = Context?.Language switch
        {
            "C#" => new[] { ".cs" },
            "Java" => new[] { ".java" },
            "Python" => new[] { ".py" },
            _ => new[] { ".cs", ".java", ".py" }
        };

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        if (files.Count == 0)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                $"No {Context?.Language ?? "source"} files found for maintainability evaluation.");
            return results;
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🧮 Running maintainability analysis on {files.Count} file(s) ({Context?.Language}/{Context?.Framework}).");

        // Track project-level aggregates
        double totalMaintainability = 0;
        double totalComplexity = 0;
        double totalCommentDensity = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
            int lineCount = content.Split('\n').Length;
            int commentCount = CountComments(content);
            int complexity = CountComplexity(content);

            // Compute maintainability index (simplified heuristic)
            double maintainabilityIndex = Math.Max(0, 100 - complexity * _policy.ComplexityWeight - lineCount / _policy.LineWeight);
            double commentDensity = lineCount > 0 ? (double)commentCount / lineCount * 100 : 0;

            totalMaintainability += maintainabilityIndex;
            totalComplexity += complexity;
            totalCommentDensity += commentDensity;

            // Emit one result per file
            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Maintainability",
                Metrics = new Dictionary<string, double>
                {
                    ["MaintainabilityIndex"] = maintainabilityIndex,
                    ["Complexity"] = complexity,
                    ["LineCount"] = lineCount,
                    ["CommentDensity"] = commentDensity,
                    ["MinMaintainabilityThreshold"] = _policy.MinMaintainabilityIndex,
                    ["MinCommentDensityThreshold"] = _policy.MinCommentDensity
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown",
                    ["RequireCommentDensityCheck"] = _policy.RequireCommentDensityCheck.ToString(),
                    ["ComplexityWeight"] = _policy.ComplexityWeight.ToString(),
                    ["LineWeight"] = _policy.LineWeight.ToString()
                }
            });
        }

        // 📊 Global summary metrics
        int fileCount = results.Count;
        if (fileCount > 0)
        {
            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "MaintainabilitySummary",
                Metrics = new Dictionary<string, double>
                {
                    ["FileCount"] = fileCount,
                    ["AverageMaintainability"] = totalMaintainability / fileCount,
                    ["AverageComplexity"] = totalComplexity / fileCount,
                    ["AverageCommentDensity"] = totalCommentDensity / fileCount
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown"
                }
            });
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
            $"✅ Maintainability analysis complete — {results.Count} metric entries collected.");

        return results;
    }

    // -------------------------------------------------
    // Helpers
    // -------------------------------------------------
    private static int CountComplexity(string content)
    {
        var keywords = new[] { "if", "for", "while", "switch", "case", "catch", "&&", "||" };
        return keywords.Sum(k => Regex.Matches(content, $@"\b{k}\b").Count);
    }

    private static int CountComments(string content)
    {
        var pattern = @"(\/\/.*?$|\/\*[\s\S]*?\*\/|#.*?$)";
        return Regex.Matches(content, pattern, RegexOptions.Multiline).Count;
    }
}
