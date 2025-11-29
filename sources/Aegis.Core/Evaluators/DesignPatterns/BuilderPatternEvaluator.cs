using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Evaluates Builder pattern compliance across supported languages.
/// Collects metrics for fluent chaining, Build() presence, and immutability adherence.
/// </summary>
public sealed class BuilderPatternEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "BuilderPatternEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Java", "TypeScript", "Python"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring", "Angular", "FastAPI", "Generic"];

    private static readonly Regex BuilderClassRx = new(@"class\s+(\w+Builder)\b", RegexOptions.Compiled);
    private static readonly Regex BuildMethodRx = new(@"\bBuild\s*\(", RegexOptions.Compiled);
    private static readonly Regex FluentMethodRx = new(@"public\s+\w+\s+\w+\s*\([^)]*\)\s*\{\s*return\s+this;", RegexOptions.Compiled);
    private static readonly Regex StateMutationRx = new(@"\bthis\.\w+\s*=", RegexOptions.Compiled);

    public BuilderPatternEvaluator(
        ILogger<BuilderPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns ?? new();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceBuilderPattern)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info, "🏗️ Builder pattern enforcement disabled by policy.");
            return results;
        }

        // Filter relevant file types
        var extensions = Context?.Language switch
        {
            "CSharp" => new[] { ".cs" },
            "Java" => new[] { ".java" },
            "TypeScript" => new[] { ".ts" },
            "Python" => new[] { ".py" },
            _ => new[] { ".cs", ".java", ".ts", ".py" }
        };

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        if (files.Count == 0)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info, $"No files found for Builder Pattern evaluation ({Context?.Language}).");
            return results;
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🏗️ Scanning {files.Count} files for Builder pattern metrics ({Context?.Language}/{Context?.Framework}).");

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token);
            var match = BuilderClassRx.Match(content);
            if (!match.Success) continue;

            var builderName = match.Groups[1].Value;

            bool hasBuildMethod = BuildMethodRx.IsMatch(content);
            int fluentMethods = FluentMethodRx.Matches(content).Count;
            int mutationCount = StateMutationRx.Matches(content).Count;
            bool isImmutable = content.Contains("readonly", StringComparison.OrdinalIgnoreCase)
                               || content.Contains("Immutable", StringComparison.OrdinalIgnoreCase);

            double mutationSeverityRatio = mutationCount / (double)Math.Max(1, _policy.MaxBuilderMutations);
            double fluentRatio = fluentMethods > 0 ? 1.0 : 0.0;

            // Create metrics entry
            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "DesignPattern",
                Metrics = new Dictionary<string, double>
                {
                    ["HasBuildMethod"] = hasBuildMethod ? 1 : 0,
                    ["FluentMethods"] = fluentMethods,
                    ["MutationCount"] = mutationCount,
                    ["MutationSeverityRatio"] = mutationSeverityRatio,
                    ["FluentRatio"] = fluentRatio,
                    ["IsImmutable"] = isImmutable ? 1 : 0
                },
                Metadata = new Dictionary<string, string>
                {
                    ["BuilderClassName"] = builderName,
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown",
                    ["Policy_EnforceBuilderPattern"] = _policy.EnforceBuilderPattern.ToString(),
                    ["Policy_MaxBuilderMutations"] = _policy.MaxBuilderMutations.ToString()
                }
            });
        }

        // 🧩 Optional summary: Builder quality index
        if (results.Count > 0)
        {
            var avgFluentRatio = results.Average(r => r.Metrics.GetValueOrDefault("FluentRatio", 0));
            var avgMutationSeverity = results.Average(r => r.Metrics.GetValueOrDefault("MutationSeverityRatio", 0));
            var avgImmutability = results.Average(r => r.Metrics.GetValueOrDefault("IsImmutable", 0));

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "DesignPatternSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["BuilderCount"] = results.Count,
                    ["AverageFluentRatio"] = avgFluentRatio,
                    ["AverageMutationSeverity"] = avgMutationSeverity,
                    ["AverageImmutability"] = avgImmutability,
                    ["BuilderQualityIndex"] = ComputeBuilderQuality(avgFluentRatio, avgImmutability, avgMutationSeverity)
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.EnforceBuilderPattern.ToString()
                }
            });
        }

        AegisDiagnostics.Report(Name,
            results.Count > 0 ? DiagnosticLevel.Info : DiagnosticLevel.Warning,
            $"🏗️ Builder pattern evaluation completed with {results.Count} metric entries.");

        return results;
    }

    private static double ComputeBuilderQuality(double fluent, double immutability, double mutation)
    {
        // Weighted quality index (higher = better compliance)
        double score = fluent * 0.4 + immutability * 0.4 + (1 - Math.Min(mutation, 1)) * 0.2;
        return Math.Round(score * 100, 2);
    }
}
