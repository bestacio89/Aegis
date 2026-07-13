using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd;

/// <summary>
/// Quantitatively evaluates Angular (.ts) sources for structure, naming,
/// and modular discipline.
/// Produces AngularComplianceScore (0–100) and component-level metrics.
/// </summary>
public sealed class AngularEvaluator : BaseArchitectureEvaluator
{
    private readonly FrontendPolicy _policy;


    public override string Name =>
        nameof(AngularEvaluator);


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
            @"selector\s*:\s*['""]([^'""]+)['""]",
            RegexOptions.Compiled);



    private static readonly Regex TemplateInlineRx =
        new(
            @"template\s*:\s*`([^`]*)`",
            RegexOptions.Compiled);



    private static readonly Regex ClassDeclarationRx =
        new(
            @"class\s+(\w+)",
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
            _logger.LogInformation(
                "🅰️ No Angular TypeScript files found.");

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
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to read Angular file {File}",
                    file);

                continue;
            }



            var fileName =
                Path.GetFileName(file);



            var isComponent =
                ComponentRx.IsMatch(content);



            var isModule =
                ModuleRx.IsMatch(content);



            var selectorScore = 1d;
            var complexityScore = 1d;
            var namingScore = 1d;
            var modularityScore = 1d;



            if (_policy.EnforceSelectorNaming &&
                isComponent)
            {
                selectorScore =
                    EvaluateSelector(content);
            }



            if (_policy.MaxComponentComplexity > 0 &&
                isComponent)
            {
                complexityScore =
                    EvaluateTemplateComplexity(content);
            }



            if (_policy.EnforceComponentPascalCase &&
                isComponent)
            {
                namingScore =
                    EvaluateComponentNaming(content);
            }



            if (isModule)
            {
                modularityScore =
                    EvaluateModuleStructure(content);
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
                        ["FilePath"] =
                    file,
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
                results
                    .Where(
                        r =>
                            r.Category == "Frontend")
                    .ToList();



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
                            _policy.EnforceSelectorNaming ||
                            _policy.EnforceComponentPascalCase ||
                            _policy.MaxComponentComplexity > 0 ||
                            _policy.MaxComponentsPerModule > 0
                                ? "True"
                                : "False",

                        ["Framework"] =
                            Context?.Framework
                            ?? "Angular"
                    }
                });
        }



        _logger.LogInformation(
            "🅰️ {Evaluator} completed with {Count} metric entries",
            Name,
            results.Count);



        return results;
    }



    private double EvaluateSelector(
        string content)
    {
        var match =
            SelectorRx.Match(content);


        if (!match.Success)
        {
            return 0;
        }


        return _policy.AllowedSelectorPrefixes.Any(
            prefix =>
                match.Groups[1]
                    .Value
                    .StartsWith(
                        prefix,
                        StringComparison.Ordinal))
            ? 1d
            : 0.5d;
    }



    private double EvaluateTemplateComplexity(
        string content)
    {
        var match =
            TemplateInlineRx.Match(content);


        if (!match.Success)
        {
            return 1d;
        }


        var lines =
            match.Groups[1]
                .Value
                .Split('\n')
                .Length;


        if (lines <= _policy.MaxComponentComplexity)
        {
            return 1d;
        }


        return Math.Max(
            0,
            1 -
            (double)lines /
            (_policy.MaxComponentComplexity * 2));
    }



    private static double EvaluateComponentNaming(
        string content)
    {
        var match =
            ClassDeclarationRx.Match(content);


        if (!match.Success)
        {
            return 0;
        }


        var className =
            match.Groups[1].Value;


        return char.IsUpper(className[0])
            ? 1d
            : 0.5d;
    }



    private double EvaluateModuleStructure(
        string content)
    {
        if (content.Contains(
                "bootstrap:",
                StringComparison.Ordinal))
        {
            return 0;
        }


        if (_policy.MaxComponentsPerModule <= 0)
        {
            return 1d;
        }


        foreach (Match declaration in DeclarationsRx.Matches(content))
        {
            var count =
                declaration.Groups[1]
                    .Value
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Length;


            if (count > _policy.MaxComponentsPerModule)
            {
                return Math.Max(
                    0,
                    1 -
                    (double)count /
                    (_policy.MaxComponentsPerModule * 2));
            }
        }


        return 1d;
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