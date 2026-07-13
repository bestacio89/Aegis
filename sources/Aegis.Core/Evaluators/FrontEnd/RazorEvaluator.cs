using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd;


/// <summary>
/// Quantitatively evaluates ASP.NET Razor (.cshtml) views for:
/// - Presentation separation
/// - ViewModel discipline
/// - Rendering complexity
/// - Data access isolation
/// - Dependency discipline
/// - UI componentization
///
/// Produces RazorComplianceScore (0-100)
/// and RazorHealthIndex.
/// </summary>
public sealed class RazorEvaluator : BaseArchitectureEvaluator
{
    private readonly FrontendPolicy _policy;


    public override string Name =>
        "RazorEvaluator";


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
        new(
            @"@\s*model\s+([\w\.]+)",
            RegexOptions.Compiled);



    private static readonly Regex InlineCodeBlockRx =
        new(
            @"@\s*\{",
            RegexOptions.Compiled);



    private static readonly Regex ConditionalRx =
        new(
            @"@\s*(if|else|for|foreach|while)\b",
            RegexOptions.Compiled);



    private static readonly Regex EntityModelRx =
        new(
            @"@\s*model\s+.*(Entity|Model)\b",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex ViewModelRx =
        new(
            @"@\s*model\s+.*ViewModel\b",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex DatabaseLeakRx =
        new(
            @"\b(DbContext|SqlConnection|EntityFramework|DbSet|ExecuteSql)\b",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex ServiceInjectionRx =
        new(
            @"@\s*inject\s+",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex TagHelperRx =
        new(
            @"<[\w\-]+\s",
            RegexOptions.Compiled);



    private static readonly Regex HtmlHelperRx =
        new(
            @"Html\.(Action|Partial|RenderPartial|Display|Editor)",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    public RazorEvaluator(
        ILogger<RazorEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Frontend
            ?? new FrontendPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();


        var files =
            ResolveSourceFiles(
                projectPath,
                ".cshtml");



        if (files.Count == 0)
        {
            return results;
        }



        _logger.LogInformation(
            "🟦 Running {Evaluator} on {Count} Razor views",
            Name,
            files.Count);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            string content;


            try
            {
                content =
                    await File.ReadAllTextAsync(
                        file,
                        token);
            }
            catch
            {
                continue;
            }



            var analysis =
                AnalyzeView(
                    content);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    Category =
                        "Frontend",

                    Metrics =
                    {
                        ["ComplexityScore"] =
                            analysis.ComplexityScore,

                        ["PresentationSeparationScore"] =
                            analysis.SeparationScore,

                        ["ViewModelScore"] =
                            analysis.ModelScore,

                        ["DataIsolationScore"] =
                            analysis.DataIsolationScore,

                        ["DependencyScore"] =
                            analysis.DependencyScore,

                        ["ComponentizationScore"] =
                            analysis.ComponentizationScore,

                        ["RazorComplianceScore"] =
                            analysis.ComplianceScore
                    },

                    Metadata =
                    {
                        ["FilePath"] =
                    file,
                        ["FileName"] =
                            Path.GetFileName(file),

                        ["Framework"] =
                            Context?.Framework
                            ?? "ASP.NET Razor",

                        ["Language"] =
                            Context?.Language
                            ?? "Razor",

                        ["LineCount"] =
                            analysis.LineCount.ToString(),

                        ["HasModel"] =
                            analysis.HasModel.ToString(),

                        ["UsesViewModel"] =
                            analysis.UsesViewModel.ToString(),

                        ["EntityExposure"] =
                            analysis.ExposesEntity.ToString(),

                        ["ConditionalCount"] =
                            analysis.ConditionalCount.ToString()
                    }
                });
        }



        if (results.Count > 0)
        {
            AddSummary(
                results,
                projectPath);
        }



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} results",
            Name,
            results.Count);



        return results;
    }



    private RazorMetrics AnalyzeView(
        string content)
    {
        var lineCount =
            content.Count(
                c => c == '\n') + 1;



        var hasModel =
            ModelDeclarationRx.IsMatch(content);


        var usesViewModel =
            ViewModelRx.IsMatch(content);


        var exposesEntity =
            EntityModelRx.IsMatch(content);


        var hasInlineCode =
            InlineCodeBlockRx.IsMatch(content);


        var hasDatabaseLeak =
            DatabaseLeakRx.IsMatch(content);


        var injectsServices =
            ServiceInjectionRx.IsMatch(content);


        var usesTagHelpers =
            TagHelperRx.IsMatch(content);


        var usesHtmlHelpers =
            HtmlHelperRx.IsMatch(content);



        var conditionalCount =
            ConditionalRx.Matches(content).Count;



        var complexityScore =
            CalculateComplexity(
                lineCount);



        var separationScore =
            hasInlineCode
                ? 0.5
                : 1;



        var modelScore =
            usesViewModel
                ? 1
                : exposesEntity
                    ? 0.2
                    : 0.7;



        var dataIsolationScore =
            hasDatabaseLeak
                ? 0
                : 1;



        var dependencyScore =
            injectsServices
                ? 0.5
                : 1;



        var componentizationScore =
            usesTagHelpers || usesHtmlHelpers
                ? 1
                : 0.8;



        return new RazorMetrics
        {
            LineCount = lineCount,

            HasModel = hasModel,

            UsesViewModel = usesViewModel,

            ExposesEntity = exposesEntity,

            ConditionalCount = conditionalCount,

            ComplexityScore = complexityScore,

            SeparationScore = separationScore,

            ModelScore = modelScore,

            DataIsolationScore = dataIsolationScore,

            DependencyScore = dependencyScore,

            ComponentizationScore = componentizationScore,

            ComplianceScore =
                ComputeCompliance(
                    complexityScore,
                    separationScore,
                    modelScore,
                    dataIsolationScore,
                    dependencyScore,
                    componentizationScore)
        };
    }



    private double CalculateComplexity(
        int lineCount)
    {
        if (_policy.MaxComponentComplexity <= 0 ||
            lineCount <= _policy.MaxComponentComplexity)
        {
            return 1;
        }


        return Math.Max(
            0,
            1 -
            (double)lineCount /
            (_policy.MaxComponentComplexity * 2));
    }



    private static void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var viewResults =
            results.ToList();



        var averageCompliance =
            viewResults.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "RazorComplianceScore",
                        0));



        var averageComplexity =
            viewResults.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "ComplexityScore",
                        0));



        var averageIsolation =
            viewResults.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "DataIsolationScore",
                        0));



        results.Add(
            new ArchitectureEvaluatorResult(
                "RazorEvaluator",
                projectPath)
            {
                Category =
                    "FrontendSummary",

                Metrics =
                {
                    ["RazorViewCount"] =
                        viewResults.Count,

                    ["AverageComplianceScore"] =
                        averageCompliance,

                    ["AverageComplexityScore"] =
                        averageComplexity,

                    ["AverageDataIsolation"] =
                        averageIsolation,

                    ["RazorHealthIndex"] =
                        averageCompliance * 0.7 +
                        averageIsolation * 100 * 0.3
                },

                Metadata =
                {
                    ["Evaluator"] =
                        "RazorEvaluator",

                    ["Framework"] =
                        "ASP.NET Razor"
                }
            });
    }



    private static double ComputeCompliance(
        double complexity,
        double separation,
        double model,
        double isolation,
        double dependency,
        double componentization)
    {
        var score =
            complexity * 0.20 +
            separation * 0.20 +
            model * 0.20 +
            isolation * 0.20 +
            dependency * 0.10 +
            componentization * 0.10;


        return Math.Round(
            score * 100,
            2);
    }



    private sealed class RazorMetrics
    {
        public int LineCount { get; init; }

        public bool HasModel { get; init; }

        public bool UsesViewModel { get; init; }

        public bool ExposesEntity { get; init; }

        public int ConditionalCount { get; init; }

        public double ComplexityScore { get; init; }

        public double SeparationScore { get; init; }

        public double ModelScore { get; init; }

        public double DataIsolationScore { get; init; }

        public double DependencyScore { get; init; }

        public double ComponentizationScore { get; init; }

        public double ComplianceScore { get; init; }
    }
}