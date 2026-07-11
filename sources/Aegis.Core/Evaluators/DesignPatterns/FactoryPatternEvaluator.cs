using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.DesignPatterns;


/// <summary>
/// Evaluates Factory Pattern compliance:
/// - Detects factory abstractions.
/// - Measures direct instantiation leakage.
/// - Validates interface-based creation.
/// - Detects infrastructure coupling.
/// - Produces deterministic metrics consumed by RuleEngine.
/// </summary>
public sealed class FactoryPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;


    public override string Name =>
        "FactoryPatternEvaluator";


    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript",
        "Python"
    ];


    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring",
        "Angular",
        "Flask",
        "Generic"
    ];



    private static readonly Regex FactoryClassRegex =
        new(
            @"class\s+(\w*Factory)\b",
            RegexOptions.Compiled);



    private static readonly Regex InterfaceRegex =
        new(
            @"interface\s+(I\w*Factory)\b",
            RegexOptions.Compiled);



    private static readonly Regex NewObjectRegex =
        new(
            @"\bnew\s+\w+\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex ReturnConcreteRegex =
        new(
            @"return\s+new\s+\w+\s*\(",
            RegexOptions.Compiled);



    private static readonly string[] InfrastructureIndicators =
    [
        "HttpClient",
        "FileStream",
        "SqlConnection",
        "DbContext",
        "Repository"
    ];



    public FactoryPatternEvaluator(
        ILogger<FactoryPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();


        if (!_policy.EnforceFactoryPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Info,
                "🏭 Factory pattern evaluation disabled by policy.");

            return results;
        }



        var files =
            ResolveSourceFiles(
                projectPath,
                ".cs",
                ".java",
                ".ts",
                ".py");



        if (files.Count == 0)
        {
            return results;
        }



        var contents =
            new Dictionary<string, string>();


        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            try
            {
                contents[file] =
                    await File.ReadAllTextAsync(
                        file,
                        token);
            }
            catch
            {
                continue;
            }
        }



        var interfaces =
            contents.Values
                .SelectMany(
                    content =>
                        InterfaceRegex
                            .Matches(content)
                            .Select(
                                match =>
                                    match.Groups[1].Value))
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);



        foreach (var entry in contents)
        {
            token.ThrowIfCancellationRequested();


            foreach (Match factoryMatch in FactoryClassRegex.Matches(entry.Value))
            {
                var factoryName =
                    factoryMatch.Groups[1].Value;



                var expectedInterface =
                    $"I{factoryName}";



                var hasInterface =
                    interfaces.Contains(
                        expectedInterface);



                var instantiationCount =
                    NewObjectRegex
                        .Matches(entry.Value)
                        .Count;



                var returnsConcrete =
                    ReturnConcreteRegex
                        .IsMatch(entry.Value);



                var infrastructureLeakCount =
                    InfrastructureIndicators.Count(
                        indicator =>
                            entry.Value.Contains(
                                indicator,
                                StringComparison.OrdinalIgnoreCase));



                var namingValid =
                    !_policy.RequirePatternSuffix ||
                    factoryName.EndsWith(
                        _policy.FactorySuffix,
                        StringComparison.Ordinal);



                var abstractionScore =
                    hasInterface
                        ? 1d
                        : 0d;



                var instantiationRatio =
                    instantiationCount /
                    (double)Math.Max(
                        1,
                        _policy.MaxFactoryInstantiations);



                var leakRatio =
                    infrastructureLeakCount > 0
                        ? 1d
                        : 0d;



                var namingPenalty =
                    namingValid
                        ? 0d
                        : 1d;



                var concretePenalty =
                    returnsConcrete
                        ? 1d
                        : 0d;



                var compliance =
                    ComputeCompliance(
                        abstractionScore,
                        instantiationRatio,
                        leakRatio,
                        namingPenalty,
                        concretePenalty);



                results.Add(
                    new ArchitectureEvaluatorResult(
                        Name,
                        entry.Key)
                    {
                        Category =
                            nameof(
                                ArchitectureRuleCategory.DesignPatterns),

                        Metrics =
                        {
                            ["AbstractionScore"] =
                                abstractionScore,

                            ["InstantiationCount"] =
                                instantiationCount,

                            ["InstantiationRatio"] =
                                instantiationRatio,

                            ["InfrastructureLeakCount"] =
                                infrastructureLeakCount,

                            ["ReturnsConcrete"] =
                                returnsConcrete
                                    ? 1
                                    : 0,

                            ["NamingPenalty"] =
                                namingPenalty,

                            ["FactoryComplianceScore"] =
                                compliance
                        },

                        Metadata =
                        {
                            ["FactoryName"] =
                                factoryName,

                            ["ExpectedInterface"] =
                                expectedInterface,

                            ["Language"] =
                                Context?.Language
                                ?? "Unknown",

                            ["Framework"] =
                                Context?.Framework
                                ?? "Unknown"
                        }
                    });
            }
        }



        if (results.Count > 0)
        {
            var factoryResults =
                results.ToList();



            var averageScore =
                factoryResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "FactoryComplianceScore",
                            0));



            var leakCount =
                factoryResults.Count(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "InfrastructureLeakCount",
                            0) > 0);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    projectPath)
                {
                    Category =
                        "DesignPatternSummary",

                    Metrics =
                    {
                        ["FactoryCount"] =
                            factoryResults.Count,

                        ["AverageComplianceScore"] =
                            averageScore,

                        ["InfrastructureLeakCount"] =
                            leakCount,

                        ["OverallFactoryHealth"] =
                            averageScore *
                            (
                                1 -
                                leakCount /
                                (double)Math.Max(
                                    1,
                                    factoryResults.Count)
                            )
                    },

                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["PolicyEnabled"] =
                            _policy.EnforceFactoryPattern.ToString()
                    }
                });
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"🏭 Factory evaluation completed with {results.Count} entries.");


        return results;
    }



    private static double ComputeCompliance(
        double abstraction,
        double instantiationRatio,
        double leakRatio,
        double namingPenalty,
        double concretePenalty)
    {
        var score =
            abstraction * 0.4 +
            (1 - Math.Min(instantiationRatio, 1)) * 0.25 +
            (1 - leakRatio) * 0.15 +
            (1 - namingPenalty) * 0.1 +
            (1 - concretePenalty) * 0.1;


        return Math.Round(
            score * 100,
            2);
    }
}