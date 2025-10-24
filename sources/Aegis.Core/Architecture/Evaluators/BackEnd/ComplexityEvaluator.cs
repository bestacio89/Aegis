using Aegis.Core.Architecture.Diagnostics;
using Aegis.Core.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Enums;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Core.Architecture.Evaluators.BackEnd;

/// <summary>
/// Analyzes cyclomatic complexity and method length across source files.
/// Emits EvaluatorResults with raw metrics, interpreted later by the RuleEngine.
/// </summary>
public sealed class ComplexityEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly ComplexityPolicy _policy;

    public override string Name => "ComplexityEvaluator";

    public override string[] SupportedLanguages => ["CSharp", "Java", "Python"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring Boot", "FastAPI"];

    public ComplexityEvaluator(
        ILogger<ComplexityEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Complexity ?? new ComplexityPolicy();
    }

    /// <summary>
    /// Scans source files to measure complexity and method line count.
    /// Returns EvaluatorResults instead of RuleResults.
    /// </summary>
    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        // 🔍 Determine file extensions based on language
        var extensions = Context?.Language switch
        {
            "CSharp" => new[] { ".cs" },
            "Java" => new[] { ".java" },
            "Python" => new[] { ".py" },
            _ => new[] { ".cs", ".java", ".py" }
        };

        var sourceFiles = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        if (sourceFiles.Count == 0)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                $"No {Context?.Language ?? "source"} files found for complexity analysis.");
            return results;
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🧩 Running complexity analysis on {sourceFiles.Count} file(s) ({Context?.Language}/{Context?.Framework}).");

        foreach (var file in sourceFiles)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token);

            // Split the file into method-like blocks
            var methods = Regex.Matches(content, @"\b(public|private|protected|def)\b[\s\S]*?\{[\s\S]*?\}")
                .Select(m => m.Value)
                .ToList();

            if (methods.Count == 0)
                continue;

            foreach (var methodBlock in methods)
            {
                var lineCount = methodBlock.Split('\n').Length;
                var complexity = CountDecisionPoints(methodBlock);

                // Emit raw metrics (no severity)
                results.Add(new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "Complexity",
                    Metrics = new Dictionary<string, double>
                    {
                        ["CyclomaticComplexity"] = complexity,
                        ["MethodLineCount"] = lineCount,
                        ["MaxCyclomaticThreshold"] = _policy.MaxCyclomaticComplexity,
                        ["MaxLinesThreshold"] = _policy.MaxLinesPerMethod
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["Language"] = Context?.Language ?? "Unknown",
                        ["Framework"] = Context?.Framework ?? "Unknown"
                    }
                });
            }
        }

        AegisDiagnostics.Report(Name,
            results.Count > 0 ? DiagnosticLevel.Trace : DiagnosticLevel.Info,
            $"✅ Complexity evaluation completed with {results.Count} metric entries.");

        return results;
    }

    // ------------------------------------------------
    // Helpers
    // ------------------------------------------------
    private static int CountDecisionPoints(string code)
    {
        // Rough cyclomatic complexity approximation by counting decision keywords
        var keywords = new[] { "if", "for", "while", "switch", "case", "catch", "?", "&&", "||" };
        return keywords.Sum(k => Regex.Matches(code, $@"\b{k}\b").Count);
    }
}
