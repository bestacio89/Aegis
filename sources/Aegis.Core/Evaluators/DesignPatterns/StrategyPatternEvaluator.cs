using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Quantitatively evaluates Strategy pattern implementation quality.
/// Measures adherence to polymorphism, interface abstraction, class isolation,
/// and naming convention. Produces a StrategyComplianceScore (0–100).
/// </summary>
public sealed class StrategyPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;


    public override string Name =>
        "StrategyPatternEvaluator";


    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "TypeScript"
    ];


    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring",
        "NestJS",
        "FastAPI"
    ];



    private static readonly Regex StrategyClassRx =
        new(
            @"class\s+(\w+Strategy)\b",
            RegexOptions.Compiled);



    private static readonly Regex InterfaceRx =
        new(
            @"interface\s+I?\w*Strategy\b",
            RegexOptions.Compiled);



    private static readonly Regex SwitchOrIfRx =
        new(
            @"\b(switch|if\s*\(.*Strategy.*\))",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex MultiStrategyRx =
        new(
            @"class\s+\w+\b[^{]*{[^}]*class\s+\w+Strategy\b",
            RegexOptions.Singleline |
            RegexOptions.Compiled);



    public StrategyPatternEvaluator(
        ILogger<StrategyPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();


        if (!_policy.EnforceStrategyPattern)
        {
            _logger.LogInformation(
                "🎯 Strategy pattern enforcement disabled by policy.");

            return results;
        }



        var files =
            ResolveSourceFiles(
                projectPath,
                ".cs",
                ".java",
                ".py",
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



            var hasStrategy =
                StrategyClassRx.IsMatch(content);



            var hasInterface =
                InterfaceRx.IsMatch(content);



            var usesSwitchLogic =
                SwitchOrIfRx.IsMatch(content);



            var hasMultipleStrategies =
                MultiStrategyRx.IsMatch(content);



            var correctNaming =
                fileName.EndsWith(
                    _policy.StrategySuffix +
                    Path.GetExtension(file),
                    StringComparison.OrdinalIgnoreCase);



            if (!hasStrategy && !usesSwitchLogic)
            {
                continue;
            }



            var interfaceAdherence =
                hasInterface
                    ? 1
                    : 0;



            var conditionalPenalty =
                usesSwitchLogic
                    ? 1
                    : 0;



            var isolationScore =
                hasMultipleStrategies
                    ? 0
                    : 1;



            var namingCompliance =
                correctNaming
                    ? 1
                    : 0;



            var complianceScore =
                ComputeCompliance(
                    interfaceAdherence,
                    isolationScore,
                    namingCompliance,
                    conditionalPenalty);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    Category =
                        "DesignPattern",

                    Metrics =
                    {
                        ["HasStrategy"] =
                            hasStrategy
                                ? 1
                                : 0,

                        ["InterfaceAdherence"] =
                            interfaceAdherence,

                        ["IsolationScore"] =
                            isolationScore,

                        ["ConditionalPenalty"] =
                            conditionalPenalty,

                        ["NamingCompliance"] =
                            namingCompliance,

                        ["StrategyComplianceScore"] =
                            complianceScore
                    },

                    Metadata =
                    {
                        ["FileName"] =
                            fileName,

                        ["Language"] =
                            Context?.Language
                            ?? "Unknown",

                        ["Framework"] =
                            Context?.Framework
                            ?? "Unknown",

                        ["HasInterface"] =
                            hasInterface.ToString(),

                        ["UsesSwitchLogic"] =
                            usesSwitchLogic.ToString(),

                        ["MultipleStrategiesInFile"] =
                            hasMultipleStrategies.ToString(),

                        ["PatternSuffix"] =
                            _policy.StrategySuffix
                    }
                });
        }



        if (results.Count > 0)
        {
            var strategyResults =
                results.ToList();



            var averageScore =
                strategyResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "StrategyComplianceScore",
                            0));



            var averageInterface =
                strategyResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "InterfaceAdherence",
                            0));



            var conditionalViolations =
                strategyResults.Count(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "ConditionalPenalty",
                            0) == 1);



            var isolationViolations =
                strategyResults.Count(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "IsolationScore",
                            0) == 0);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    projectPath)
                {
                    Category =
                        "DesignPatternSummary",

                    Metrics =
                    {
                        ["StrategyCount"] =
                            strategyResults.Count,

                        ["AverageComplianceScore"] =
                            averageScore,

                        ["AverageInterfaceAdherence"] =
                            averageInterface,

                        ["ConditionalViolationCount"] =
                            conditionalViolations,

                        ["MultiStrategyViolationCount"] =
                            isolationViolations,

                        ["OverallStrategyHealth"] =
                            averageScore *
                            (1 -
                             (conditionalViolations +
                              isolationViolations) /
                             (double)Math.Max(
                                 1,
                                 strategyResults.Count))
                    },

                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["PolicyEnabled"] =
                            _policy.EnforceStrategyPattern.ToString()
                    }
                });
        }



        _logger.LogInformation(
            "🎯 {Evaluator} completed with {Count} metric entries",
            Name,
            results.Count);



        return results;
    }



    private static double ComputeCompliance(
        double interfaceAdherence,
        double isolationScore,
        double namingCompliance,
        double conditionalPenalty)
    {
        var score =
            interfaceAdherence * 0.35 +
            isolationScore * 0.25 +
            namingCompliance * 0.15 +
            (1 - conditionalPenalty) * 0.25;


        return Math.Round(
            score * 100,
            2);
    }
}