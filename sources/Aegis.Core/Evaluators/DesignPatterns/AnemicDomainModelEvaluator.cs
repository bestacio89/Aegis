using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Aegis.Shared.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Detects **Anemic Domain Models** — classes with excessive state and little to no behavior.
/// Emits structured metrics for RuleEngine pattern intelligence.
/// </summary>
public sealed class AnemicDomainModelEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "AnemicDomainModelEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Java"];
    public override string[] SupportedFrameworks => ["Domain", "DDD", "CleanArchitecture"];

    private static readonly Regex ClassRx =
        new(@"class\s+([A-Z][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static readonly Regex PropertyRx =
        new(@"(public|private|protected)\s+(\w+(\?|<\w+>)?)\s+\w+\s*\{[^\}]*\}", RegexOptions.Compiled);

    private static readonly Regex MethodRx =
        new(@"(public|protected|internal)\s+(\w+(\?|<\w+>)?)\s+\w+\s*\(.*\)\s*\{", RegexOptions.Compiled);

    public AnemicDomainModelEvaluator(ILogger<AnemicDomainModelEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.DesignPatterns ?? new DesignPatternPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.DetectAnemicDomainModels)
        {
            _logger.LogInformation("🏗️ {Evaluator} disabled by policy.", Name);
            return results;
        }

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".java", StringComparison.OrdinalIgnoreCase))
            .Where(f => !PathUtils.IsExcludedDir(f))
            .ToList();

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;
            try { content = await File.ReadAllTextAsync(file, token); }
            catch { continue; }

            // 🧩 Ignore known data types
            if (content.Contains("record ", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("Dto", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("Config", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (Match match in ClassRx.Matches(content))
            {
                var className = match.Groups[1].Value;

                int properties = PropertyRx.Matches(content).Count;
                int methods = MethodRx.Matches(content).Count;

                bool allPublic = content.Contains("public ", StringComparison.OrdinalIgnoreCase)
                                 && !content.Contains("private set", StringComparison.OrdinalIgnoreCase);
                bool inheritsBase = content.Contains("BaseEntity", StringComparison.OrdinalIgnoreCase);

                double propertyToMethodRatio = properties == 0 ? 0 : (double)methods / properties;
                double anemicScore = ComputeAnemicScore(properties, methods);

                // 🧠 Build metric result
                results.Add(new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "DesignPattern",
                    Metrics = new Dictionary<string, double>
                    {
                        ["Properties"] = properties,
                        ["Methods"] = methods,
                        ["PropertyToMethodRatio"] = propertyToMethodRatio,
                        ["AnemicScore"] = anemicScore
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["ClassName"] = className,
                        ["Language"] = Context?.Language ?? "Unknown",
                        ["Framework"] = Context?.Framework ?? "Unknown",
                        ["InheritsBaseEntity"] = inheritsBase.ToString(),
                        ["AllPublicProperties"] = allPublic.ToString(),
                        ["Policy_MinProperties"] = _policy.MinPropertiesForDomainClass.ToString(),
                        ["Policy_MaxAllowedMethods"] = _policy.MaxAllowedMethodsForAnemic.ToString()
                    }
                });
            }
        }

        // 📊 Global summary: average anemic index
        if (results.Count > 0)
        {
            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "DesignPatternSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["ClassCount"] = results.Count,
                    ["AverageAnemicScore"] = results.Average(r => r.Metrics.GetValueOrDefault("AnemicScore", 0))
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.DetectAnemicDomainModels.ToString()
                }
            });
        }

        _logger.LogInformation("🏗️ {Evaluator} completed with {Count} entries", Name, results.Count);
        return results;
    }

    private static double ComputeAnemicScore(int properties, int methods)
    {
        // Simple heuristic: the higher the properties/methods ratio, the more anemic
        if (properties == 0) return 0;
        double ratio = (double)methods / properties;
        double score = 1 - Math.Min(ratio, 1.0); // 1.0 = fully anemic, 0.0 = rich in behavior
        return Math.Round(score * 100, 2);
    }
}
