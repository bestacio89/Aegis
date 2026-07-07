using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Evaluates Factory pattern compliance:
/// - Validates abstraction (interface presence)
/// - Detects excessive direct instantiation
/// - Flags concrete return types and dependency leaks
/// Produces quantitative metrics for architecture analysis.
/// </summary>
public sealed class FactoryPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "FactoryPatternEvaluator";
    public override string[] SupportedLanguages => ["C#", "Java", "TypeScript", "Python"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring", "Angular", "Flask", "Generic"];

    private static readonly Regex FactoryClassRx = new(@"class\s+(\w+Factory)\b", RegexOptions.Compiled);
    private static readonly Regex ReturnNewRx = new(@"\breturn\s+new\s+\w+\s*\(", RegexOptions.Compiled);
    private static readonly Regex NewOperatorRx = new(@"\bnew\s+\w+\s*\(", RegexOptions.Compiled);
    private static readonly Regex InterfaceRx = new(@"interface\s+I(\w+Factory)\b", RegexOptions.Compiled);

    public FactoryPatternEvaluator(ILogger<FactoryPatternEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns ?? new();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceFactoryPattern)
        {
            _logger.LogInformation("🏭 Factory pattern enforcement disabled by policy.");
            return results;
        }

        var extensions = Context?.Language switch
        {
            "C#" => new[] { ".cs" },
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
            _logger.LogInformation("🏭 No relevant files found for Factory pattern evaluation.");
            return results;
        }

        _logger.LogTrace("🏭 Scanning {Count} files for Factory pattern compliance...", files.Count);

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token);
            var match = FactoryClassRx.Match(content);
            if (!match.Success) continue;

            var factoryName = match.Groups[1].Value;
            var directory = Path.GetDirectoryName(file)!;

            // --- Core detections ---
            bool hasInterface = Directory.GetFiles(directory, $"I{factoryName}.*", SearchOption.AllDirectories).Any();
            int instantiations = NewOperatorRx.Matches(content).Count;
            bool returnsConcrete = ReturnNewRx.IsMatch(content);
            bool leaksInfrastructure = content.Contains("HttpClient") || content.Contains("FileStream") || content.Contains("SqlConnection");
            bool validNaming = !_policy.RequirePatternSuffix || factoryName.EndsWith(_policy.FactorySuffix);

            // --- Derived metrics ---
            double abstractionScore = hasInterface ? 1.0 : 0.0;
            double instantiationRatio = instantiations / (double)Math.Max(1, _policy.MaxFactoryInstantiations);
            double leakPenalty = leaksInfrastructure ? 1.0 : 0.0;
            double namingPenalty = validNaming ? 0.0 : 0.5;
            double returnPenalty = returnsConcrete ? 0.5 : 0.0;

            // 🧮 Compute compliance score (0–100)
            double compliance = ComputeCompliance(abstractionScore, instantiationRatio, leakPenalty, namingPenalty, returnPenalty);

            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "DesignPattern",
                Metrics = new Dictionary<string, double>
                {
                    ["AbstractionScore"] = abstractionScore,
                    ["InstantiationCount"] = instantiations,
                    ["InstantiationRatio"] = instantiationRatio,
                    ["ReturnsConcrete"] = returnsConcrete ? 1 : 0,
                    ["InfrastructureLeak"] = leaksInfrastructure ? 1 : 0,
                    ["NamingPenalty"] = namingPenalty,
                    ["FactoryComplianceScore"] = compliance
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FactoryName"] = factoryName,
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown",
                    ["Policy_MaxFactoryInstantiations"] = _policy.MaxFactoryInstantiations.ToString(),
                    ["Policy_RequireFactoryInterface"] = _policy.RequireFactoryInterface.ToString(),
                    ["Policy_RequirePatternSuffix"] = _policy.RequirePatternSuffix.ToString()
                }
            });
        }

        // 📊 Global summary
        if (results.Count > 0)
        {
            double avgScore = results.Average(r => r.Metrics.GetValueOrDefault("FactoryComplianceScore", 0));
            double infraLeaks = results.Count(r => r.Metrics.GetValueOrDefault("InfrastructureLeak", 0) == 1);
            double concreteReturns = results.Count(r => r.Metrics.GetValueOrDefault("ReturnsConcrete", 0) == 1);

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "DesignPatternSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["FactoryCount"] = results.Count,
                    ["AverageComplianceScore"] = avgScore,
                    ["InfrastructureLeakCount"] = infraLeaks,
                    ["ConcreteReturnCount"] = concreteReturns,
                    ["OverallFactoryHealth"] = avgScore * (1 - (infraLeaks + concreteReturns) / Math.Max(1.0, results.Count))
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.EnforceFactoryPattern.ToString()
                }
            });
        }

        _logger.LogInformation("🏭 {Evaluator} completed with {Count} metric entries", Name, results.Count);
        return results;
    }

    private static double ComputeCompliance(double abstraction, double instantiationRatio, double leakPenalty, double namingPenalty, double returnPenalty)
    {
        // Lower penalties = higher compliance
        double score = abstraction * 0.4 +
                       (1 - Math.Min(instantiationRatio, 1)) * 0.25 +
                       (1 - leakPenalty) * 0.15 +
                       (1 - namingPenalty) * 0.1 +
                       (1 - returnPenalty) * 0.1;

        return Math.Round(score * 100, 2);
    }
}
