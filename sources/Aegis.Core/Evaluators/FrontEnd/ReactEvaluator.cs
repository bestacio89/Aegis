using System.Text.RegularExpressions;
using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Aegis.Shared.Models.Policies;
using Aegis.Shared.Models.Policies.FrontEnd;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Core.Evaluators.FrontEnd;

/// <summary>
/// Quantitatively evaluates React (.jsx/.tsx/.js/.ts) components for
/// hook correctness, prop typing, naming, and complexity.
/// Produces a ReactComplianceScore (0–100) per component and aggregates
/// project-wide ReactHealthIndex.
/// </summary>
public sealed class ReactEvaluator : BaseEvaluator
{
    private readonly FrontendPolicy _policy;

    public override string Name => "ReactEvaluator";
    public override string[] SupportedLanguages => ["TypeScript", "JavaScript"];
    public override string[] SupportedFrameworks => ["React"];

    // React-specific regex patterns
    private static readonly Regex HookRx = new(@"\buse[A-Z]\w*\s*\(", RegexOptions.Compiled);
    private static readonly Regex FunctionComponentRx = new(@"(function|const)\s+([A-Z][A-Za-z0-9]*)", RegexOptions.Compiled);
    private static readonly Regex NonPascalCaseRx = new(@"(function|const)\s+([a-z]\w*)", RegexOptions.Compiled);
    private static readonly Regex PropTypeRx = new(@"[Pp]rop[Tt]ypes\s*=", RegexOptions.Compiled);
    private static readonly Regex TsInterfaceRx = new(@"interface\s+[A-Z][A-Za-z0-9_]*\s*\{", RegexOptions.Compiled);
    private static readonly Regex ClassLifecycleRx = new(@"componentDid(Mount|Update|Unmount)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ReactEvaluator(ILogger<ReactEvaluator> logger, IOptions<AegisPolicy> options)
        : base(logger)
    {
        _policy = options.Value.Frontend ?? new FrontendPolicy();
    }

    protected override async Task<IEnumerable<EvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<EvaluatorResult>();
        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f =>
                f.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        _logger.LogInformation("⚛️ Running {Evaluator} on {Count} files", Name, files.Count);

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token);
            bool isJs = file.EndsWith(".js") || file.EndsWith(".jsx");
            bool hasProps = content.Contains("props", StringComparison.OrdinalIgnoreCase);

            // 🧩 Base metrics
            double hookDisciplineScore = 1.0;
            double namingScore = 1.0;
            double typingScore = 1.0;
            double complexityScore = 1.0;
            double modernizationScore = 1.0;

            // 1️⃣ Hook rule violations
            if (_policy.CheckHooksRules && HookRx.IsMatch(content) && content.Contains("if (", StringComparison.Ordinal))
                hookDisciplineScore = 0.0;

            // 2️⃣ Legacy class lifecycle usage
            if (ClassLifecycleRx.IsMatch(content))
                modernizationScore = 0.5;

            // 3️⃣ PascalCase naming
            if (_policy.EnforceComponentPascalCase && NonPascalCaseRx.IsMatch(content))
                namingScore = 0.5;

            // 4️⃣ Prop typing (PropTypes or TypeScript interfaces)
            if (hasProps)
            {
                if (isJs && !PropTypeRx.IsMatch(content))
                    typingScore = 0.5;
                if (!isJs && !TsInterfaceRx.IsMatch(content))
                    typingScore = 0.5;
            }

            // 5️⃣ Component size & complexity
            var lineCount = content.Split('\n').Length;
            if (_policy.MaxComponentComplexity > 0 && lineCount > _policy.MaxComponentComplexity)
                complexityScore = Math.Max(0, 1 - ((double)lineCount / (_policy.MaxComponentComplexity * 2)));

            // 6️⃣ Hook density (too many hooks)
            var hookCount = HookRx.Matches(content).Count;
            if (_policy.MaxHooksPerComponent > 0 && hookCount > _policy.MaxHooksPerComponent)
                hookDisciplineScore *= Math.Max(0.5, 1 - (hookCount / (double)(_policy.MaxHooksPerComponent * 2)));

            // 🧮 Composite compliance score
            double complianceScore = ComputeCompliance(hookDisciplineScore, namingScore, typingScore, complexityScore, modernizationScore);

            results.Add(new EvaluatorResult(Name, file)
            {
                Category = "Frontend",
                Metrics = new Dictionary<string, double>
                {
                    ["HookDisciplineScore"] = hookDisciplineScore,
                    ["NamingScore"] = namingScore,
                    ["TypingScore"] = typingScore,
                    ["ComplexityScore"] = complexityScore,
                    ["ModernizationScore"] = modernizationScore,
                    ["ReactComplianceScore"] = complianceScore
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = Path.GetFileName(file),
                    ["Language"] = isJs ? "JavaScript" : "TypeScript",
                    ["Framework"] = "React",
                    ["HookCount"] = hookCount.ToString(),
                    ["LineCount"] = lineCount.ToString()
                }
            });
        }

        // 📈 Aggregate project summary
        if (results.Count > 0)
        {
            double avgCompliance = results.Average(r => r.Metrics.GetValueOrDefault("ReactComplianceScore", 0));
            double avgHookDiscipline = results.Average(r => r.Metrics.GetValueOrDefault("HookDisciplineScore", 0));
            double avgTyping = results.Average(r => r.Metrics.GetValueOrDefault("TypingScore", 0));
            double avgComplexity = results.Average(r => r.Metrics.GetValueOrDefault("ComplexityScore", 0));

            results.Add(new EvaluatorResult(Name, projectPath)
            {
                Category = "FrontendSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["ReactFileCount"] = results.Count,
                    ["AverageComplianceScore"] = avgCompliance,
                    ["AverageHookDiscipline"] = avgHookDiscipline,
                    ["AverageTyping"] = avgTyping,
                    ["AverageComplexity"] = avgComplexity,
                    ["ReactHealthIndex"] = (avgCompliance * 0.6) + (avgHookDiscipline * 0.4)
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["Framework"] = "React",
                    ["PolicyEnabled"] = _policy.CheckHooksRules.ToString()
                }
            });
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} results", Name, results.Count);
        return results;
    }

    private static double ComputeCompliance(double hookDiscipline, double naming, double typing, double complexity, double modernization)
    {
        // Weighted scoring emphasizing modernity & hook correctness
        double score = (hookDiscipline * 0.3) +
                       (naming * 0.15) +
                       (typing * 0.2) +
                       (complexity * 0.2) +
                       (modernization * 0.15);

        return Math.Round(score * 100, 2);
    }
}
