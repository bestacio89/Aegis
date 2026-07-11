using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.FrontEnd;

/// <summary>
/// Quantitatively evaluates Angular (.ts) sources for structure, naming, and modular discipline.
/// Produces AngularComplianceScore (0–100) and component-level metrics for maintainability.
/// </summary>
public sealed class AngularEvaluator : BaseArchitectureEvaluator
{
    private readonly FrontendPolicy _policy;


    public override string Name =>
        "AngularEvaluator";


    public override string[] SupportedLanguages =>
    [
        "TypeScript"
    ];


    public override string[] SupportedFrameworks =>
    [
        "Angular"
    ];



    private static readonly Regex ComponentRx =
        new(
            @"@Component\s*\(\s*\{",
            RegexOptions.Compiled);



    private static readonly Regex ModuleRx =
        new(
            @"@NgModule\s*\(\s*\{",
            RegexOptions.Compiled);



    private static readonly Regex SelectorRx =
        new(
            @"selector\s*:\s*'([^']+)'",
            RegexOptions.Compiled);



    private static readonly Regex TemplateInlineRx =
        new(
            @"template\s*:\s*`([^`]*)`",
            RegexOptions.Compiled);



    private static readonly Regex PascalCaseRx =
        new(
            @"class\s+([a-z]\w*)\s+implements\s+OnInit",
            RegexOptions.Compiled);



    private static readonly Regex DeclarationsRx =
        new(
            @"declarations\s*:\s*\[([^\]]+)\]",
            RegexOptions.Singleline |
            RegexOptions.Compiled);



    public AngularEvaluator(
        ILogger<AngularEvaluator> logger,
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
                ".ts");



        if (files.Count == 0)
        {
            return results;
        }



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



            var fileName =
                Path.GetFileName(file);



            var isComponent =
                ComponentRx.IsMatch(content);



            var isModule =
                ModuleRx.IsMatch(content);



            var selectorScore = 1.0;
            var complexityScore = 1.0;
            var namingScore = 1.0;
            var modularityScore = 1.0;



            if (_policy.EnforceSelectorNaming &&
                isComponent)
            {
                var selectorMatch =
                    SelectorRx.Match(content);


                if (!selectorMatch.Success)
                {
                    selectorScore = 0;
                }
                else
                {
                    var selector =
                        selectorMatch.Groups[1].Value;


                    if (!_policy.AllowedSelectorPrefixes.Any(
                            p =>
                                selector.StartsWith(
                                    p,
                                    StringComparison.Ordinal)))
                    {
                        selectorScore = 0.5;
                    }
                }
            }



            if (_policy.MaxComponentComplexity > 0 &&
                TemplateInlineRx.IsMatch(content))
            {
                var template =
                    TemplateInlineRx
                        .Match(content)
                        .Groups[1]
                        .Value;


                var lineCount =
                    template.Split('\n').Length;


                if (lineCount > _policy.MaxComponentComplexity)
                {
                    complexityScore =
                        Math.Max(
                            0,
                            1 -
                            (double)lineCount /
                            (_policy.MaxComponentComplexity * 2));
                }
            }



            if (_policy.EnforceComponentPascalCase &&
                PascalCaseRx.IsMatch(content))
            {
                namingScore = 0.5;
            }



            if (isModule &&
                content.Contains(
                    "bootstrap:",
                    StringComparison.Ordinal))
            {
                modularityScore = 0;
            }



            if (_policy.MaxComponentsPerModule > 0 &&
                isModule)
            {
                foreach (Match declaration in DeclarationsRx.Matches(content))
                {
                    var count =
                        declaration.Groups[1]
                            .Value
                            .Split(',')
                            .Length;


                    if (count > _policy.MaxComponentsPerModule)
                    {
                        modularityScore =
                            Math.Max(
                                0,
                                1 -
                                (double)count /
                                (_policy.MaxComponentsPerModule * 2));
                    }
                }
            }



            var complianceScore =
                ComputeCompliance(
                    selectorScore,
                    complexityScore,
                    namingScore,
                    modularityScore);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    Category =
                        "Frontend",

                    Metrics =
                    {
                        ["SelectorScore"] =
                            selectorScore,

                        ["ComplexityScore"] =
                            complexityScore,

                        ["NamingScore"] =
                            namingScore,

                        ["ModularityScore"] =
                            modularityScore,

                        ["AngularComplianceScore"] =
                            complianceScore
                    },

                    Metadata =
                    {
                        ["FileName"] =
                            fileName,

                        ["Language"] =
                            Context?.Language
                            ?? "TypeScript",

                        ["Framework"] =
                            Context?.Framework
                            ?? "Angular",

                        ["IsComponent"] =
                            isComponent.ToString(),

                        ["IsModule"] =
                            isModule.ToString()
                    }
                });
        }



        if (results.Count > 0)
        {
            var angularResults =
                results.ToList();



            var averageCompliance =
                angularResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "AngularComplianceScore",
                            0));



            var averageComplexity =
                angularResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "ComplexityScore",
                            0));



            var averageSelector =
                angularResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "SelectorScore",
                            0));



            var averageModularity =
                angularResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "ModularityScore",
                            0));



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    projectPath)
                {
                    Category =
                        "FrontendSummary",

                    Metrics =
                    {
                        ["AngularFileCount"] =
                            angularResults.Count,

                        ["AverageComplianceScore"] =
                            averageCompliance,

                        ["AverageComplexityScore"] =
                            averageComplexity,

                        ["AverageSelectorScore"] =
                            averageSelector,

                        ["AverageModularityScore"] =
                            averageModularity,

                        ["FrontEndHealthIndex"] =
                            averageCompliance * 0.6 +
                            averageModularity * 0.4
                    },

                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["PolicyEnabled"] =
                            "True",

                        ["Framework"] =
                            Context?.Framework
                            ?? "Angular"
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
        double selector,
        double complexity,
        double naming,
        double modularity)
    {
        var score =
            selector * 0.25 +
            complexity * 0.25 +
            naming * 0.20 +
            modularity * 0.30;


        return Math.Round(
            score * 100,
            2);
    }
}