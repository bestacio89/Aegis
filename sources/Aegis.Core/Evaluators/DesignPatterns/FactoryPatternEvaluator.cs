using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.DesignPatterns;


public sealed class FactoryPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
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
        "Flask"
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



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        if (Context is null)
            return results;



        if (!_policy.EnforceFactoryPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Factory pattern evaluation disabled.");

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
            return results;



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
            catch (Exception ex)
            {
                AegisDiagnostics.Report(
                    Name,
                    DiagnosticLevel.Warning,
                    $"Unable to read {file}.",
                    ex);
            }
        }



        var interfaces =
            contents.Values
                .SelectMany(
                    content =>
                        InterfaceRegex
                            .Matches(content)
                            .Select(
                                x =>
                                    x.Groups[1].Value))
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);



        foreach (var source in contents)
        {
            token.ThrowIfCancellationRequested();


            foreach (Match match in FactoryClassRegex.Matches(source.Value))
            {
                var factoryName =
                    match.Groups[1].Value;


                var expectedInterface =
                    $"I{factoryName}";


                var hasInterface =
                    interfaces.Contains(
                        expectedInterface);


                var instantiations =
                    NewObjectRegex
                        .Matches(source.Value)
                        .Count;


                var returnsConcrete =
                    ReturnConcreteRegex
                        .IsMatch(source.Value);


                var infrastructureLeaks =
                    InfrastructureIndicators.Count(
                        indicator =>
                            source.Value.Contains(
                                indicator,
                                StringComparison.OrdinalIgnoreCase));



                results.Add(
                    CreateResult(
                        source.Key,
                        factoryName,
                        expectedInterface,
                        hasInterface,
                        instantiations,
                        returnsConcrete,
                        infrastructureLeaks));
            }
        }



        if (results.Count > 0)
        {
            results.Add(
                CreateSummary(
                    projectPath,
                    results));
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Factory evaluation completed with {results.Count} entries.");



        return results;
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        string factoryName,
        string expectedInterface,
        bool hasInterface,
        int instantiations,
        bool returnsConcrete,
        int infrastructureLeaks)
    {
        var instantiationRatio =
            instantiations /
            (double)Math.Max(
                1,
                _policy.MaxFactoryInstantiations);



        var compliance =
            ComputeCompliance(
                hasInterface ? 1 : 0,
                instantiationRatio,
                infrastructureLeaks > 0 ? 1 : 0,
                returnsConcrete ? 1 : 0);



        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            ProjectName =
                Context!.ProjectName,

            Language =
                Context.Language,

            Framework =
                Context.Framework,


            Layer =
                ResolveLayer(file),


            DetectionConfidence =
                Context.Confidence,


            Category =
                "DesignPattern",


            Metrics =
            {
                ["AbstractionScore"] =
                    hasInterface ? 1 : 0,

                ["InstantiationCount"] =
                    instantiations,

                ["InstantiationRatio"] =
                    instantiationRatio,

                ["InfrastructureLeakCount"] =
                    infrastructureLeaks,

                ["ReturnsConcrete"] =
                    returnsConcrete ? 1 : 0,

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
                    Context.Language,

                ["Framework"] =
                    Context.Framework ?? "Unknown"
            }
        };
    }



    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        IEnumerable<ArchitectureEvaluatorResult> results)
    {
        var items =
            results.ToList();



        return new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            ProjectName =
                Context?.ProjectName ?? string.Empty,


            Category =
                "DesignPatternSummary",


            Metrics =
            {
                ["FactoryCount"] =
                    items.Count,

                ["AverageComplianceScore"] =
                    items.Average(
                        x =>
                            x.Metrics.GetValueOrDefault(
                                "FactoryComplianceScore"))
            },


            Metadata =
            {
                ["Evaluator"] =
                    Name
            }
        };
    }



    private string ResolveLayer(
        string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(
                layer =>
                    layer.Files.Contains(
                        file,
                        StringComparer.OrdinalIgnoreCase))
            ?.Name
            ??
            "Unknown";
    }



    private static double ComputeCompliance(
        double abstraction,
        double instantiationRatio,
        double leakRatio,
        double concretePenalty)
    {
        var score =
            abstraction * 0.4 +
            (1 - Math.Min(instantiationRatio, 1)) * 0.25 +
            (1 - leakRatio) * 0.15 +
            (1 - concretePenalty) * 0.2;


        return Math.Round(
            score * 100,
            2);
    }
}