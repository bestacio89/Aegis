using System.Text.RegularExpressions;
using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Aegis.Shared.Models.Policies;
using Aegis.Shared.Models.Policies.FrontEnd;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Core.Evaluators.FrontEnd;

/// <summary>
/// Quantitatively evaluates Angular (.ts) sources for structure, naming, and modular discipline.
/// Produces AngularComplianceScore (0–100) and component-level metrics for maintainability.
/// </summary>
public sealed class AngularEvaluator : BaseEvaluator
{
    private readonly FrontendPolicy _policy;

    public override string Name => "AngularEvaluator";
    public override string[] SupportedLanguages => ["TypeScript"];
    public override string[] SupportedFrameworks => ["Angular"];

    // Regex patterns
    private static readonly Regex ComponentRx = new(@"@Component\s*\(\s*\{", RegexOptions.Compiled);
    private static readonly Regex ModuleRx = new(@"@NgModule\s*\(\s*\{", RegexOptions.Compiled);
    private static readonly Regex SelectorRx = new(@"selector\s*:\s*'([^']+)'", RegexOptions.Compiled);
    private static readonly Regex TemplateInlineRx = new(@"template\s*:\s*`([^`]*)`", RegexOptions.Compiled);
    private static readonly Regex PascalCaseRx = new(@"class\s+([a-z]\w*)\s+implements\s+OnInit", RegexOptions.Compiled);
    private static readonly Regex DeclarationsRx = new(@"declarations\s*:\s*\[([^\]]+)\]", RegexOptions.Singleline | RegexOptions.Compiled);

    public AngularEvaluator(ILogger<AngularEvaluator> logger, IOptions<AegisPolicy> options)
        : base(logger)
    {
        _policy = options.Value.Frontend ?? new FrontendPolicy();
    }

    protected override async Task<IEnumerable<EvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<EvaluatorResult>();
        var files = Directory.EnumerateFiles(projectPath, "*.ts", SearchOption.AllDirectories)
                             .Where(f => !IsExcludedDir(f))
                             .ToList();

        _logger.LogInformation("🅰️ Running {Evaluator} on {Count} files", Name, files.Count);

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token);
            var fileName = Path.GetFileName(file);

            bool isComponent = ComponentRx.IsMatch(content);
            bool isModule = ModuleRx.IsMatch(content);

            // Derived metrics
            double selectorScore = 1.0;
            double complexityScore = 1.0;
            double namingScore = 1.0;
            double modularityScore = 1.0;

            // 1️⃣ Component selector naming
            if (_policy.EnforceSelectorNaming && isComponent)
            {
                var match = SelectorRx.Match(content);
                if (!match.Success)
                {
                    selectorScore = 0;
                }
                else
                {
                    var selector = match.Groups[1].Value;
                    if (!_policy.AllowedSelectorPrefixes.Any(p => selector.StartsWith(p, StringComparison.Ordinal)))
                        selectorScore = 0.5; // wrong prefix
                }
            }

            // 2️⃣ Inline template complexity
            if (_policy.MaxComponentComplexity > 0 && TemplateInlineRx.IsMatch(content))
            {
                var template = TemplateInlineRx.Match(content).Groups[1].Value;
                var lineCount = template.Split('\n').Length;
                if (lineCount > _policy.MaxComponentComplexity)
                    complexityScore = Math.Max(0, 1 - ((double)lineCount / (_policy.MaxComponentComplexity * 2)));
            }

            // 3️⃣ Component PascalCase check
            if (_policy.EnforceComponentPascalCase && PascalCaseRx.IsMatch(content))
                namingScore = 0.5;

            // 4️⃣ Root vs Shared module misuse
            if (isModule && content.Contains("bootstrap:", StringComparison.Ordinal))
                modularityScore = 0.0;

            // 5️⃣ Overly large module detection
            if (_policy.MaxComponentsPerModule > 0 && isModule)
            {
                foreach (Match decl in DeclarationsRx.Matches(content))
                {
                    var count = decl.Groups[1].Value.Split(',').Length;
                    if (count > _policy.MaxComponentsPerModule)
                        modularityScore = Math.Max(0, 1 - ((double)count / (_policy.MaxComponentsPerModule * 2)));
                }
            }

            // 🎯 Final Angular compliance score
            double angularComplianceScore = ComputeCompliance(selectorScore, complexityScore, namingScore, modularityScore);

            results.Add(new EvaluatorResult(Name, file)
            {
                Category = "Frontend",
                Metrics = new Dictionary<string, double>
                {
                    ["SelectorScore"] = selectorScore,
                    ["ComplexityScore"] = complexityScore,
                    ["NamingScore"] = namingScore,
                    ["ModularityScore"] = modularityScore,
                    ["AngularComplianceScore"] = angularComplianceScore
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = fileName,
                    ["Language"] = "TypeScript",
                    ["Framework"] = "Angular",
                    ["IsComponent"] = isComponent.ToString(),
                    ["IsModule"] = isModule.ToString()
                }
            });
        }

        // 📊 Aggregate Summary
        if (results.Count > 0)
        {
            double avgCompliance = results.Average(r => r.Metrics.GetValueOrDefault("AngularComplianceScore", 0));
            double avgComplexity = results.Average(r => r.Metrics.GetValueOrDefault("ComplexityScore", 0));
            double avgSelector = results.Average(r => r.Metrics.GetValueOrDefault("SelectorScore", 0));
            double avgModularity = results.Average(r => r.Metrics.GetValueOrDefault("ModularityScore", 0));

            results.Add(new EvaluatorResult(Name, projectPath)
            {
                Category = "FrontendSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["AngularFileCount"] = results.Count,
                    ["AverageComplianceScore"] = avgCompliance,
                    ["AverageComplexityScore"] = avgComplexity,
                    ["AverageSelectorScore"] = avgSelector,
                    ["AverageModularityScore"] = avgModularity,
                    ["FrontEndHealthIndex"] = (avgCompliance * 0.6) + (avgModularity * 0.4)
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = "True",
                    ["Framework"] = "Angular"
                }
            });
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} metric entries", Name, results.Count);
        return results;
    }

    private static double ComputeCompliance(double selector, double complexity, double naming, double modularity)
    {
        // Weighted average emphasizing maintainability and correctness
        double score = (selector * 0.25) + (complexity * 0.25) + (naming * 0.20) + (modularity * 0.30);
        return Math.Round(score * 100, 2);
    }
}
