using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd;

/// <summary>
/// Quantitatively evaluates ASP.NET Razor (.cshtml) views for
/// presentation separation, view model discipline, complexity,
/// and server-side rendering architecture.
///
/// Produces RazorComplianceScore (0-100)
/// and project-wide RazorHealthIndex.
/// </summary>
public sealed class RazorEvaluator : BaseArchitectureEvaluator
{
    private readonly FrontendPolicy _policy;


    public override string Name => "RazorEvaluator";

    public override string[] SupportedLanguages =>
    [
        "Razor",
        "C#"
    ];

    public override string[] SupportedFrameworks =>
    [
        "ASP.NET MVC",
        "ASP.NET Razor Pages"
    ];


    private static readonly Regex ModelDeclarationRx =
        new(@"@\s*model\s+([\w\.]+)",
            RegexOptions.Compiled);


    private static readonly Regex InlineCodeBlockRx =
        new(@"@\s*\{",
            RegexOptions.Compiled);


    private static readonly Regex ConditionalRx =
        new(@"@\s*(if|else|for|foreach|while)\b",
            RegexOptions.Compiled);


    private static readonly Regex EntityModelRx =
        new(@"@\s*model\s+.*(Entity|Model)\b",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);


    private static readonly Regex ViewModelRx =
        new(@"@\s*model\s+.*ViewModel\b",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);


    private static readonly Regex DatabaseLeakRx =
        new(@"\b(DbContext|SqlConnection|EntityFramework|DbSet|ExecuteSql)\b",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);


    private static readonly Regex ServiceInjectionRx =
        new(@"@\s*inject\s+",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);


    private static readonly Regex TagHelperRx =
        new(@"<[\w\-]+\s",
            RegexOptions.Compiled);


    private static readonly Regex HtmlHelperRx =
        new(@"Html\.(Action|Partial|RenderPartial|Display|Editor)",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    public RazorEvaluator(
        ILogger<RazorEvaluator> logger,
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


        var files =
            Directory.EnumerateFiles(
                    projectPath,
                    "*.cshtml",
                    SearchOption.AllDirectories)
                .Where(f => !IsExcludedDir(f))
                .ToList();



        _logger.LogInformation(
            "🟦 Running {Evaluator} on {Count} Razor views",
            Name,
            files.Count);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            var content =
                await File.ReadAllTextAsync(file, token);


            var lineCount =
                content.Split('\n').Length;


            var modelMatch =
                ModelDeclarationRx.Match(content);


            bool hasModel =
                modelMatch.Success;


            bool usesViewModel =
                ViewModelRx.IsMatch(content);


            bool exposesEntity =
                EntityModelRx.IsMatch(content);


            bool hasInlineCode =
                InlineCodeBlockRx.IsMatch(content);


            bool hasDatabaseLeak =
                DatabaseLeakRx.IsMatch(content);


            bool injectsServices =
                ServiceInjectionRx.IsMatch(content);


            bool usesTagHelpers =
                TagHelperRx.IsMatch(content);


            bool usesHtmlHelpers =
                HtmlHelperRx.IsMatch(content);


            int conditionalCount =
                ConditionalRx.Matches(content).Count;



            // Complexity

            double complexityScore = 1.0;

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



            // Presentation separation

            double separationScore =
                hasInlineCode
                    ? 0.5
                    : 1.0;



            // Model discipline

            double modelScore =
                usesViewModel
                    ? 1.0
                    : exposesEntity
                        ? 0.2
                        : 0.7;



            // Data access isolation

            double dataIsolationScore =
                hasDatabaseLeak
                    ? 0.0
                    : 1.0;



            // Service injection

            double dependencyScore =
                injectsServices
                    ? 0.5
                    : 1.0;



            // Reusable UI elements

            double componentizationScore =
                usesTagHelpers || usesHtmlHelpers
                    ? 1.0
                    : 0.8;



            double complianceScore =
                ComputeCompliance(
                    complexityScore,
                    separationScore,
                    modelScore,
                    dataIsolationScore,
                    dependencyScore,
                    componentizationScore);



            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Frontend",

                Metrics = new Dictionary<string, double>
                {
                    ["ComplexityScore"] = complexityScore,
                    ["PresentationSeparationScore"] = separationScore,
                    ["ViewModelScore"] = modelScore,
                    ["DataIsolationScore"] = dataIsolationScore,
                    ["DependencyScore"] = dependencyScore,
                    ["ComponentizationScore"] = componentizationScore,
                    ["RazorComplianceScore"] = complianceScore
                },


                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = Path.GetFileName(file),
                    ["Framework"] = "ASP.NET Razor",
                    ["Language"] = "Razor",
                    ["LineCount"] = lineCount.ToString(),
                    ["HasModel"] = hasModel.ToString(),
                    ["UsesViewModel"] = usesViewModel.ToString(),
                    ["EntityExposure"] = exposesEntity.ToString(),
                    ["ConditionalCount"] = conditionalCount.ToString()
                }
            });
        }



        if (results.Count > 0)
        {
            double avgCompliance =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "RazorComplianceScore",
                        0));


            double avgComplexity =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "ComplexityScore",
                        0));


            double avgIsolation =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "DataIsolationScore",
                        0));



            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "FrontendSummary",

                Metrics = new Dictionary<string, double>
                {
                    ["RazorViewCount"] = results.Count,
                    ["AverageComplianceScore"] = avgCompliance,
                    ["AverageComplexityScore"] = avgComplexity,
                    ["AverageDataIsolation"] = avgIsolation,

                    ["RazorHealthIndex"] =
                        avgCompliance * 0.7 +
                        avgIsolation * 100 * 0.3
                },


                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["Framework"] = "ASP.NET Razor"
                }
            });
        }



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} results",
            Name,
            results.Count);


        return results;
    }



    private static double ComputeCompliance(
        double complexity,
        double separation,
        double model,
        double isolation,
        double dependency,
        double componentization)
    {
        double score =
            complexity * 0.20 +
            separation * 0.20 +
            model * 0.20 +
            isolation * 0.20 +
            dependency * 0.10 +
            componentization * 0.10;


        return Math.Round(score * 100, 2);
    }
}