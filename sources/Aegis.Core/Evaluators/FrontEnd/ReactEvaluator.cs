using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd;

/// <summary>
/// Quantitatively evaluates React (.jsx/.tsx/.js/.ts) components for:
/// - Hook discipline
/// - Component naming conventions
/// - Prop typing discipline
/// - Component complexity
/// - Legacy lifecycle usage
///
/// Produces ReactComplianceScore (0-100)
/// and project-level ReactHealthIndex.
/// </summary>
public sealed class ReactEvaluator : BaseArchitectureEvaluator
{
    private readonly FrontendPolicy _policy;

    public override string Name => "ReactEvaluator";

    public override string[] SupportedLanguages =>
    [
        "TypeScript",
        "JavaScript"
    ];

    public override string[] SupportedFrameworks =>
    [
        "React"
    ];

    private static readonly Regex HookRx =
        new(@"\buse[A-Z]\w*\s*\(",
            RegexOptions.Compiled);

    private static readonly Regex NonPascalComponentRx =
        new(@"(function|const)\s+[a-z]\w*",
            RegexOptions.Compiled);

    private static readonly Regex PropTypeRx =
        new(@"[Pp]rop[Tt]ypes\s*=",
            RegexOptions.Compiled);

    private static readonly Regex TsInterfaceRx =
        new(@"interface\s+[A-Z][A-Za-z0-9_]*\s*\{",
            RegexOptions.Compiled);

    private static readonly Regex ClassLifecycleRx =
        new(@"componentDid(Mount|Update|Unmount)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);


    public ReactEvaluator(
        ILogger<ReactEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Frontend ?? new FrontendPolicy();
    }


    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        var language = Context?.Language ?? "Unknown";
        var framework = Context?.Framework ?? "Unknown";


        var files = Directory
            .EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f =>
                f.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsExcludedDir(f))
            .ToList();


        if (files.Count == 0)
        {
            _logger.LogInformation(
                "⚛️ No React files found for evaluation.");

            return results;
        }


        _logger.LogInformation(
            "⚛️ Running {Evaluator} on {Count} files",
            Name,
            files.Count);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch
            {
                continue;
            }


            var fileName = Path.GetFileName(file);

            bool isJavaScript =
                file.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);


            bool hasProps =
                content.Contains("props",
                    StringComparison.OrdinalIgnoreCase);



            double hookDisciplineScore = 1.0;
            double namingScore = 1.0;
            double typingScore = 1.0;
            double complexityScore = 1.0;
            double modernizationScore = 1.0;



            int hookCount = HookRx.Matches(content).Count;

            int lineCount =
                content.Split('\n').Length;



            // Hook rules
            if (_policy.CheckHooksRules &&
                HookRx.IsMatch(content) &&
                content.Contains("if (",
                    StringComparison.Ordinal))
            {
                hookDisciplineScore = 0;
            }



            // Legacy React classes
            if (ClassLifecycleRx.IsMatch(content))
            {
                modernizationScore = 0.5;
            }



            // Component naming
            if (_policy.EnforceComponentPascalCase &&
                NonPascalComponentRx.IsMatch(content))
            {
                namingScore = 0.5;
            }



            // Props typing
            if (hasProps)
            {
                if (isJavaScript &&
                    !PropTypeRx.IsMatch(content))
                {
                    typingScore = 0.5;
                }

                if (!isJavaScript &&
                    !TsInterfaceRx.IsMatch(content))
                {
                    typingScore = 0.5;
                }
            }



            // Component complexity
            if (_policy.MaxComponentComplexity > 0 &&
                lineCount > _policy.MaxComponentComplexity)
            {
                complexityScore =
                    Math.Max(
                        0,
                        1 -
                        (double)lineCount /
                        (_policy.MaxComponentComplexity * 2));
            }



            // Excessive hooks
            if (_policy.MaxHooksPerComponent > 0 &&
                hookCount > _policy.MaxHooksPerComponent)
            {
                hookDisciplineScore *=
                    Math.Max(
                        0.5,
                        1 -
                        hookCount /
                        (double)(_policy.MaxHooksPerComponent * 2));
            }



            double complianceScore =
                ComputeCompliance(
                    hookDisciplineScore,
                    namingScore,
                    typingScore,
                    complexityScore,
                    modernizationScore);



            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Frontend",

                Metrics = new Dictionary<string, double>
                {
                    ["HookDisciplineScore"] = hookDisciplineScore,
                    ["NamingScore"] = namingScore,
                    ["TypingScore"] = typingScore,
                    ["ComplexityScore"] = complexityScore,
                    ["ModernizationScore"] = modernizationScore,
                    ["HookCount"] = hookCount,
                    ["LineCount"] = lineCount,
                    ["ReactComplianceScore"] = complianceScore
                },


                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = fileName,
                    ["Language"] = language,
                    ["Framework"] = framework,
                    ["Layer"] = Context?.Layer ?? "Unknown",
                    ["Target"] = file,
                    ["IsJavaScript"] = isJavaScript.ToString(),
                    ["HasProps"] = hasProps.ToString()
                }
            });
        }



        if (results.Count > 0)
        {
            double avgCompliance =
                results.Average(r =>
                    r.Metrics.GetValueOrDefault(
                        "ReactComplianceScore",
                        0));


            double avgHook =
                results.Average(r =>
                    r.Metrics.GetValueOrDefault(
                        "HookDisciplineScore",
                        0));


            double avgTyping =
                results.Average(r =>
                    r.Metrics.GetValueOrDefault(
                        "TypingScore",
                        0));


            double avgComplexity =
                results.Average(r =>
                    r.Metrics.GetValueOrDefault(
                        "ComplexityScore",
                        0));



            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "FrontendSummary",

                Metrics = new Dictionary<string, double>
                {
                    ["ReactFileCount"] = files.Count,
                    ["AverageComplianceScore"] = avgCompliance,
                    ["AverageHookDiscipline"] = avgHook,
                    ["AverageTyping"] = avgTyping,
                    ["AverageComplexity"] = avgComplexity,
                    ["ReactHealthIndex"] =
                        avgCompliance * 0.6 +
                        avgHook * 0.4
                },


                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["Language"] = language,
                    ["Framework"] = framework,
                    ["Layer"] = Context?.Layer ?? "Unknown",
                    ["PolicyEnabled"] =
                        _policy.CheckHooksRules.ToString()
                }
            });
        }


        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} metric entries",
            Name,
            results.Count);


        return results;
    }



    private static double ComputeCompliance(
        double hookDiscipline,
        double naming,
        double typing,
        double complexity,
        double modernization)
    {
        double score =
            hookDiscipline * 0.30 +
            naming * 0.15 +
            typing * 0.20 +
            complexity * 0.20 +
            modernization * 0.15;


        return Math.Round(score * 100, 2);
    }
}