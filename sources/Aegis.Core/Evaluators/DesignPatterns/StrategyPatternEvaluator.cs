using System.Text.RegularExpressions;
using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Aegis.Shared.Models.Policies;
using Aegis.Shared.Models.Policies.Architecture;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Core.Evaluators.DesignPatterns;

/// <summary>
/// Quantitatively evaluates Strategy pattern implementation quality.
/// Measures adherence to polymorphism, interface abstraction, class isolation,
/// and naming convention. Produces a StrategyComplianceScore (0–100).
/// </summary>
public sealed class StrategyPatternEvaluator : BaseEvaluator
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "StrategyPatternEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Java", "Python", "TypeScript"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring", "NestJS", "FastAPI"];

    private static readonly Regex StrategyClassRx = new(@"class\s+(\w+Strategy)\b", RegexOptions.Compiled);
    private static readonly Regex InterfaceRx = new(@"interface\s+I?\w*Strategy\b", RegexOptions.Compiled);
    private static readonly Regex SwitchOrIfRx = new(@"\b(switch|if\s*\(.*Strategy.*\))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MultiStrategyRx = new(@"class\s+\w+\b[^{]*{[^}]*class\s+\w+Strategy\b", RegexOptions.Singleline | RegexOptions.Compiled);

    public StrategyPatternEvaluator(ILogger<StrategyPatternEvaluator> logger, IOptions<AegisPolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns ?? new();
    }

    protected override async Task<IEnumerable<EvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<EvaluatorResult>();

        if (!_policy.EnforceStrategyPattern)
        {
            _logger.LogInformation("🎯 Strategy pattern enforcement disabled by policy.");
            return results;
        }

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

        _logger.LogInformation("🎯 Running {Evaluator} on {Count} files", Name, files.Count);

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token);
            var fileName = Path.GetFileName(file);

            bool hasStrategy = StrategyClassRx.IsMatch(content);
            bool hasInterface = InterfaceRx.IsMatch(content);
            bool usesSwitchLogic = SwitchOrIfRx.IsMatch(content);
            bool hasMultipleStrategies = MultiStrategyRx.IsMatch(content);
            bool correctNaming = fileName.EndsWith(_policy.StrategySuffix + Path.GetExtension(file), StringComparison.OrdinalIgnoreCase);

            if (!hasStrategy && !usesSwitchLogic)
                continue;

            // 🔍 Derived metrics
            double interfaceAdherence = hasInterface ? 1.0 : 0.0;
            double conditionalPenalty = usesSwitchLogic ? 1.0 : 0.0;
            double isolationScore = hasMultipleStrategies ? 0.0 : 1.0;
            double namingCompliance = correctNaming ? 1.0 : 0.0;

            // 🎯 Compliance scoring model
            double complianceScore = ComputeCompliance(interfaceAdherence, isolationScore, namingCompliance, conditionalPenalty);

            results.Add(new EvaluatorResult(Name, file)
            {
                Category = "DesignPattern",
                Metrics = new Dictionary<string, double>
                {
                    ["HasStrategy"] = hasStrategy ? 1 : 0,
                    ["InterfaceAdherence"] = interfaceAdherence,
                    ["IsolationScore"] = isolationScore,
                    ["ConditionalPenalty"] = conditionalPenalty,
                    ["NamingCompliance"] = namingCompliance,
                    ["StrategyComplianceScore"] = complianceScore
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = fileName,
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown",
                    ["HasInterface"] = hasInterface.ToString(),
                    ["UsesSwitchLogic"] = usesSwitchLogic.ToString(),
                    ["MultipleStrategiesInFile"] = hasMultipleStrategies.ToString(),
                    ["PatternSuffix"] = _policy.StrategySuffix
                }
            });
        }

        // 📈 Aggregate summary
        if (results.Count > 0)
        {
            double avgScore = results.Average(r => r.Metrics.GetValueOrDefault("StrategyComplianceScore", 0));
            double avgInterfaceAdherence = results.Average(r => r.Metrics.GetValueOrDefault("InterfaceAdherence", 0));
            double conditionalViolations = results.Count(r => r.Metrics.GetValueOrDefault("ConditionalPenalty", 0) == 1);
            double multiImplViolations = results.Count(r => r.Metrics.GetValueOrDefault("IsolationScore", 0) == 0);

            results.Add(new EvaluatorResult(Name, projectPath)
            {
                Category = "DesignPatternSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["StrategyCount"] = results.Count,
                    ["AverageComplianceScore"] = avgScore,
                    ["AverageInterfaceAdherence"] = avgInterfaceAdherence,
                    ["ConditionalViolationCount"] = conditionalViolations,
                    ["MultiStrategyViolationCount"] = multiImplViolations,
                    ["OverallStrategyHealth"] = avgScore * (1 - ((conditionalViolations + multiImplViolations) / Math.Max(1.0, results.Count)))
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.EnforceStrategyPattern.ToString()
                }
            });
        }

        _logger.LogInformation("🎯 {Evaluator} completed with {Count} metric entries", Name, results.Count);
        return results;
    }

    private static double ComputeCompliance(double interfaceAdherence, double isolationScore, double namingCompliance, double conditionalPenalty)
    {
        // Weighted model — penalties reduce score significantly
        double score =
            (interfaceAdherence * 0.35) +
            (isolationScore * 0.25) +
            (namingCompliance * 0.15) +
            ((1 - conditionalPenalty) * 0.25);

        return Math.Round(score * 100, 2);
    }
}
