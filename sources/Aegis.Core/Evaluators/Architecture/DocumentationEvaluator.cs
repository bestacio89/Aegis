using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Evaluators.Architecture;

/// <summary>
/// Evaluates inline documentation coverage across source files.
/// Produces structured EvaluatorResults with per-file coverage metrics.
/// </summary>
public sealed class DocumentationEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    public override string Name => "DocumentationEvaluator";

    public override string[] SupportedLanguages => ["C#", "TypeScript", "JavaScript", "Java", "Python"];
    public override string[] SupportedFrameworks => ["*"];

    public DocumentationEvaluator(ILogger<DocumentationEvaluator> logger) : base(logger) { }

    /// <summary>
    /// Analyzes source files for documentation coverage and emits EvaluatorResults.
    /// </summary>
    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        // Determine file extensions by detected language
        var extensions = Context?.Language switch
        {
            "C#" => new[] { ".cs" },
            "TypeScript" => new[] { ".ts" },
            "JavaScript" => new[] { ".js" },
            "Java" => new[] { ".java" },
            "Python" => new[] { ".py" },
            _ => new[] { ".cs", ".ts", ".js", ".java", ".py" }
        };

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        if (files.Count == 0)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                $"No {Context?.Language ?? "source"} files found for documentation analysis.");
            return results;
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"📘 Analyzing {files.Count} {Context?.Language} file(s) for documentation coverage.");

        double totalCoverage = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
            var lines = content.Split('\n');
            int totalLines = lines.Length;
            int docLines = CountDocumentationLines(lines, Context?.Language ?? "C#");

            double coverage = totalLines > 0 ? (double)docLines / totalLines * 100 : 0;
            totalCoverage += coverage;

            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Documentation",
                Metrics = { ["DocumentationCoverage"] = coverage },
                Metadata = new Dictionary<string, string>
                {
                    ["File"] = Path.GetFileName(file),
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["DocLines"] = docLines.ToString(),
                    ["TotalLines"] = totalLines.ToString()
                }
            });
        }

        double avgCoverage = results.Any()
            ? totalCoverage / results.Count
            : 0;

        results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
        {
            Category = "Documentation",
            Metrics = { ["AverageDocumentationCoverage"] = avgCoverage },
            Metadata = new Dictionary<string, string>
            {
                ["Message"] = $"Average documentation coverage: {avgCoverage:0.0}%",
                ["Language"] = Context?.Language ?? "Unknown"
            }
        });

        AegisDiagnostics.Report(Name,
            avgCoverage < 10 ? DiagnosticLevel.Warning : DiagnosticLevel.Info,
            $"📖 Documentation evaluation complete. Average coverage: {avgCoverage:0.0}%.");

        return results;
    }

    // ---------------------------------------------
    // Helpers
    // ---------------------------------------------
    private static int CountDocumentationLines(IEnumerable<string> lines, string language)
    {
        return language switch
        {
            "C#" => lines.Count(l => l.TrimStart().StartsWith("///")),
            "Java" => lines.Count(l => l.TrimStart().StartsWith("*") || l.TrimStart().StartsWith("//")),
            "Python" => lines.Count(l => l.TrimStart().StartsWith("#") || l.TrimStart().StartsWith("\"\"\"")),
            "TypeScript" or "JavaScript" => lines.Count(l => l.TrimStart().StartsWith("//") || l.TrimStart().StartsWith("/*")),
            _ => lines.Count(l => l.TrimStart().StartsWith("/") || l.TrimStart().StartsWith("#"))
        };
    }
}
