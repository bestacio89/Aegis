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

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        var extensions = Context?.Language switch
        {
            "C#" => new[] { ".cs" },
            "Java" => new[] { ".java" },
            "Python" => new[] { ".py" },
            _ => new[] { ".cs", ".java", ".py" }
        };

        // Whitelist-based enumeration: Only include files under 'back' or 'front'
        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var relativePath = Path.GetRelativePath(projectPath, f);
                // Whitelist: Must start with 'back' or 'front'
                bool isTargetDomain = relativePath.StartsWith("back", StringComparison.OrdinalIgnoreCase) ||
                                     relativePath.StartsWith("front", StringComparison.OrdinalIgnoreCase);

                // Must have correct extension AND not be an excluded system directory
                return isTargetDomain &&
                       extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) &&
                       !IsExcludedDir(f);
            })
            .ToList();

        if (files.Count == 0)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                $"No source files found in 'back' or 'front' domains for maintainability evaluation.");
            return results;
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🧮 Running maintainability analysis on {files.Count} file(s) ({Context?.Language}/{Context?.Framework}).");

        double totalMaintainability = 0;
        double totalComplexity = 0;
        double totalCommentDensity = 0;

        foreach (var file in files)
        {
            _logger.LogInformation("🚀 {Evaluator} started for {Path}", Name, Path.GetFileName(file));
            token.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
            int lineCount = content.Split('\n').Length;
            int commentCount = CountComments(content);
            int complexity = CountComplexity(content);

            double maintainabilityIndex = Math.Max(0, 100 - complexity * _policy.ComplexityWeight - lineCount / _policy.LineWeight);
            double commentDensity = lineCount > 0 ? (double)commentCount / lineCount * 100 : 0;

            totalMaintainability += maintainabilityIndex;
            totalComplexity += complexity;
            totalCommentDensity += commentDensity;

            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Maintainability",
                Metrics = new Dictionary<string, double>
                {
                    ["MaintainabilityIndex"] = maintainabilityIndex,
                    ["Complexity"] = complexity,
                    ["LineCount"] = lineCount,
                    ["CommentDensity"] = commentDensity
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["FilePath"] = file
                }
            });
        }

        if (results.Count > 0)
        {
            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "MaintainabilitySummary",
                Metrics = new Dictionary<string, double>
                {
                    ["FileCount"] = results.Count,
                    ["AverageMaintainability"] = totalMaintainability / results.Count,
                    ["AverageComplexity"] = totalComplexity / results.Count,
                    ["AverageCommentDensity"] = totalCommentDensity / results.Count
                }
            });
        }

        return results;
    }

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