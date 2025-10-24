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
/// Evaluates class cohesion by analyzing member and method distribution across source files.
/// Produces EvaluatorResults with field counts, method counts, and method/field ratios.
/// </summary>
public sealed class CohesionEvaluator : BaseEvaluator, IScopedDependency
{
    private readonly CohesionPolicy _policy;

    public override string Name => "CohesionEvaluator";

    public override string[] SupportedLanguages => ["CSharp", "Java", "Python"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring Boot", "FastAPI"];

    public CohesionEvaluator(
        ILogger<CohesionEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Cohesion ?? new CohesionPolicy();
    }

    /// <summary>
    /// Core evaluation logic — gathers cohesion metrics without applying judgments.
    /// </summary>
    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        // 🔍 Filter extensions based on detected language
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
                $"No {Context?.Language ?? "source"} files found for cohesion analysis.");
            return results;
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🏗️ Running cohesion analysis on {sourceFiles.Count} files ({Context?.Language}/{Context?.Framework}).");

        foreach (var file in sourceFiles)
        {
            token.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file, token);

            foreach (Match match in Regex.Matches(content, @"class\s+([A-Za-z_][A-Za-z0-9_]*)"))
            {
                var className = match.Groups[1].Value;
                var classBlock = ExtractClassBlock(content, className);
                if (string.IsNullOrWhiteSpace(classBlock))
                    continue;

                int fieldCount = Regex.Matches(classBlock, @"\b(private|protected|public)\b.*?;").Count;
                int methodCount = Regex.Matches(classBlock, @"\b(public|private|protected)\s+\w[\w<>,\s]*\s+\w+\s*\(").Count;

                if (fieldCount == 0 && methodCount == 0)
                    continue;

                double ratio = fieldCount == 0 ? methodCount : (double)methodCount / fieldCount;
                int totalMembers = fieldCount + methodCount;

                results.Add(new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "Cohesion",
                    Metrics = new Dictionary<string, double>
                    {
                        ["FieldCount"] = fieldCount,
                        ["MethodCount"] = methodCount,
                        ["MemberCount"] = totalMembers,
                        ["MethodFieldRatio"] = ratio
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["ClassName"] = className,
                        ["Language"] = Context?.Language ?? "Unknown",
                        ["Framework"] = Context?.Framework ?? "Unknown",
                        ["Threshold_MaxMembersPerClass"] = _policy.MaxMembersPerClass.ToString(),
                        ["Threshold_MaxMethodFieldRatio"] = _policy.MaxMethodFieldRatio.ToString()
                    }
                });
            }
        }

        // 🧠 Summarize metrics
        AegisDiagnostics.Report(Name,
            results.Count > 0 ? DiagnosticLevel.Trace : DiagnosticLevel.Info,
            $"✅ Cohesion evaluation completed with {results.Count} metric entries.");

        return results;
    }

    // -------------------------------------------------
    // Helpers
    // -------------------------------------------------
    private static string ExtractClassBlock(string content, string className)
    {
        var start = content.IndexOf($"class {className}", StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        var braceCount = 0;
        for (int i = start; i < content.Length; i++)
        {
            if (content[i] == '{') braceCount++;
            if (content[i] == '}')
            {
                braceCount--;
                if (braceCount == 0)
                    return content[start..(i + 1)];
            }
        }

        return string.Empty;
    }
}
