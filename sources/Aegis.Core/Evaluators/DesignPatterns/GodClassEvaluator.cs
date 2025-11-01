using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Detects and quantifies "God Class" anti-patterns by analyzing:
/// - Class size (lines)
/// - Method density
/// - Cohesion (property vs method ratio)
/// - Anemic domain tendencies
/// Outputs metrics and an overall GodClassScore (0–100).
/// </summary>
public sealed class GodClassEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "GodClassEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Java", "Python", "TypeScript"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring", "NestJS", "FastAPI"];

    private static readonly Regex ClassRx = new(@"class\s+(\w+)\b", RegexOptions.Compiled);
    private static readonly Regex MethodRx = new(@"\b(public|private|protected|def|function)\s+\w+\s*\(", RegexOptions.Compiled);
    private static readonly Regex PropertyRx = new(@"\b(get|set|var|val|let|this\.)\w+", RegexOptions.Compiled);

    public GodClassEvaluator(ILogger<GodClassEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.DesignPatterns ?? new();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.DetectGodClasses)
        {
            _logger.LogInformation("💤 GodClassEvaluator disabled by policy.");
            return results;
        }

        var scaling = GetScalingFactor(Context?.Layer);
        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".java") || f.EndsWith(".ts") || f.EndsWith(".py"))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogInformation("💀 No source files found for God Class evaluation.");
            return results;
        }

        _logger.LogTrace("💀 Scanning {Count} files for God Class tendencies...", files.Count);

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token);
            var classMatches = ClassRx.Matches(content);

            foreach (Match match in classMatches)
            {
                var className = match.Groups[1].Value;
                int methodCount = MethodRx.Matches(content).Count;
                int propertyCount = PropertyRx.Matches(content).Count;
                int lineCount = content.Split('\n').Length;

                // Dynamic thresholds
                int maxMethods = (int)(_policy.MaxMethodsPerClass * scaling);
                int maxLines = (int)(_policy.MaxLinesPerClass * scaling);

                // Derived ratios
                double methodDensity = methodCount / (double)Math.Max(1, lineCount);
                double propertyRatio = methodCount + propertyCount > 0
                    ? (double)propertyCount / (methodCount + propertyCount)
                    : 0;

                // Raw thresholds normalized (0–1)
                double lineRatio = lineCount / (double)Math.Max(1, maxLines);
                double methodRatio = methodCount / (double)Math.Max(1, maxMethods);

                // Compute the anti-pattern severity
                double godFactor = ComputeGodFactor(lineRatio, methodRatio, propertyRatio);

                // Invert for compliance scoring (0–100)
                double complianceScore = Math.Round((1 - Math.Min(1, godFactor)) * 100, 2);

                results.Add(new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "DesignPattern",
                    Metrics = new Dictionary<string, double>
                    {
                        ["LineCount"] = lineCount,
                        ["MethodCount"] = methodCount,
                        ["PropertyCount"] = propertyCount,
                        ["LineRatio"] = lineRatio,
                        ["MethodRatio"] = methodRatio,
                        ["PropertyRatio"] = propertyRatio,
                        ["GodClassSeverity"] = godFactor,
                        ["GodClassComplianceScore"] = complianceScore
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["ClassName"] = className,
                        ["Language"] = Context?.Language ?? "Unknown",
                        ["Layer"] = Context?.Layer ?? "Unknown",
                        ["ScalingFactor"] = scaling.ToString("0.00"),
                        ["Policy_MaxLinesPerClass"] = _policy.MaxLinesPerClass.ToString(),
                        ["Policy_MaxMethodsPerClass"] = _policy.MaxMethodsPerClass.ToString()
                    }
                });
            }
        }

        // 🧮 Summary-level metric aggregation
        if (results.Count > 0)
        {
            double avgCompliance = results.Average(r => r.Metrics.GetValueOrDefault("GodClassComplianceScore", 0));
            double avgLines = results.Average(r => r.Metrics.GetValueOrDefault("LineCount", 0));
            double avgMethods = results.Average(r => r.Metrics.GetValueOrDefault("MethodCount", 0));
            double avgSeverity = results.Average(r => r.Metrics.GetValueOrDefault("GodClassSeverity", 0));

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "DesignPatternSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["ClassCount"] = results.Count,
                    ["AverageComplianceScore"] = avgCompliance,
                    ["AverageLineCount"] = avgLines,
                    ["AverageMethodCount"] = avgMethods,
                    ["AverageGodClassSeverity"] = avgSeverity,
                    ["OverallDesignHealth"] = avgCompliance * (1 - Math.Min(avgSeverity, 1))
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.DetectGodClasses.ToString()
                }
            });
        }

        _logger.LogInformation("💀 {Evaluator} completed with {Count} metric entries", Name, results.Count);
        return results;
    }

    private static double ComputeGodFactor(double lineRatio, double methodRatio, double propertyRatio)
    {
        // Weighted anti-pattern model:
        // Too many lines → architecture smell
        // Too many methods → behavioral bloat
        // Too high property ratio → low cohesion
        double score = lineRatio * 0.4 + methodRatio * 0.4 + propertyRatio * 0.2;
        return Math.Min(score, 2.0); // Cap at 2 for normalization
    }

    private double GetScalingFactor(string? layer)
    {
        if (string.IsNullOrEmpty(layer)) return 1.0;
        return _policy.LayerScaling.TryGetValue(layer, out var scale) ? scale : 1.0;
    }
}
