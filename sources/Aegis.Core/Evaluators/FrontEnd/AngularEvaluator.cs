using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd;


/// <summary>
/// Evaluates Angular TypeScript sources for:
/// - Component structure
/// - Selector conventions
/// - Component complexity
/// - Naming discipline
/// - Module organization
///
/// Produces AngularComplianceScore.
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
            @"@Component\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex ModuleRx =
        new(
            @"@NgModule\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex AngularImportRx =
        new(
            @"from\s+['""]@angular/",
            RegexOptions.Compiled);



    private static readonly Regex SelectorRx =
        new(
            @"selector\s*:\s*['""]([^'""]+)['""]",
            RegexOptions.Compiled);



    private static readonly Regex TemplateInlineRx =
        new(
            @"template\s*:\s*`([^`]*)`",
            RegexOptions.Singleline |
            RegexOptions.Compiled);



    private static readonly Regex TemplateUrlRx =
        new(
            @"templateUrl\s*:",
            RegexOptions.Compiled);



    private static readonly Regex ClassDeclarationRx =
        new(
            @"export\s+class\s+(\w+)",
            RegexOptions.Compiled);



    private static readonly Regex StandaloneRx =
        new(
            @"standalone\s*:\s*true",
            RegexOptions.Compiled);



    private static readonly Regex DeclarationsRx =
        new(
            @"declarations\s*:\s*\[([^\]]*)\]",
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
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to read Angular file {File}",
                    file);

                continue;
            }



            if (!IsAngularFile(content))
            {
                continue;
            }



            var isComponent =
                ComponentRx.IsMatch(content);


            var isModule =
                ModuleRx.IsMatch(content);



            var selectorScore =
                isComponent
                    ? EvaluateSelector(content)
                    : 1d;



            var complexityScore =
                isComponent
                    ? EvaluateComplexity(
                        content)
                    : 1d;



            var namingScore =
                EvaluateNaming(content);



            var modularityScore =
                isModule
                    ? EvaluateModuleStructure(content)
                    : 1d;



            var compliance =
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
                            compliance
                    },


                    Metadata =
                    {
                        ["FilePath"] =
                            file,

                        ["FileName"] =
                            Path.GetFileName(file),

                        ["Framework"] =
                            "Angular",

                        ["Language"] =
                            "TypeScript",

                        ["IsComponent"] =
                            isComponent.ToString(),

                        ["IsModule"] =
                            isModule.ToString(),

                        ["StandaloneComponent"] =
                            StandaloneRx.IsMatch(content)
                                .ToString()
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
            "🅰️ {Evaluator} completed with {Count} results",
            Name,
            results.Count);


        return results;
    }



    private static bool IsAngularFile(
        string content)
    {
        return AngularImportRx.IsMatch(content)
               ||
               ComponentRx.IsMatch(content)
               ||
               ModuleRx.IsMatch(content);
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


        if (!_policy.AllowedSelectorPrefixes.Any())
        {
            return 1;
        }


        return _policy.AllowedSelectorPrefixes.Any(
            prefix =>
                match.Groups[1]
                    .Value
                    .StartsWith(
                        prefix,
                        StringComparison.Ordinal))
            ? 1
            : 0.5;
    }



    private double EvaluateComplexity(
        string content)
    {
        var lines =
            content.Count(
                c => c == '\n') + 1;


        if (_policy.MaxComponentComplexity <= 0)
        {
            return 1;
        }


        if (lines <= _policy.MaxComponentComplexity)
        {
            return 1;
        }


        return Math.Max(
            0,
            1 -
            (double)lines /
            (_policy.MaxComponentComplexity * 2));
    }



    private static double EvaluateNaming(
        string content)
    {
        var match =
            ClassDeclarationRx.Match(content);


        if (!match.Success)
        {
            return 0;
        }


        var name =
            match.Groups[1]
                .Value;


        return char.IsUpper(name[0])
            ? 1
            : 0.5;
    }



    private double EvaluateModuleStructure(
        string content)
    {
        if (_policy.MaxComponentsPerModule <= 0)
        {
            return 1;
        }


        foreach (Match match in DeclarationsRx.Matches(content))
        {
            var count =
                match.Groups[1]
                    .Value
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Length;


            if (count >
                _policy.MaxComponentsPerModule)
            {
                return 0.5;
            }
        }


        return 1;
    }



    private void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var average =
            results.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "AngularComplianceScore",
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
                        results.Count,

                    ["AverageComplianceScore"] =
                        average,

                    ["FrontEndHealthIndex"] =
                        average
                },

                Metadata =
                {
                    ["Framework"] =
                        "Angular"
                }
            });
    }



    private static double ComputeCompliance(
        double selector,
        double complexity,
        double naming,
        double modularity)
    {
        var score =
            selector * 0.25 +
            complexity * 0.30 +
            naming * 0.20 +
            modularity * 0.25;


        return Math.Round(
            score * 100,
            2);
    }
}