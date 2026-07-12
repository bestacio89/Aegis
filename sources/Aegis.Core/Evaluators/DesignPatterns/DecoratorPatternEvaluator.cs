using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.DesignPatterns;


public sealed class DecoratorPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;



    public override string Name =>
        "DecoratorPatternEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript"
    ];



    public override string[] SupportedFrameworks =>
    [
        "CleanArchitecture",
        "DDD",
        "Spring",
        "NestJS",
        "Hexagonal",
        "Microservices"
    ];



    private static readonly Regex InterfaceImplementationRegex =
        new(
            @"class\s+\w+\s*:\s*\w+",
            RegexOptions.Compiled);



    private static readonly Regex InnerComponentRegex =
        new(
            @"\b(private|protected)\s+\w+\s+_?\w*(Service|Component|Handler)\b",
            RegexOptions.Compiled);



    private static readonly Regex ConstructorInjectionRegex =
        new(
            @"\b(public|this)\s*\w*\(.*(Service|Component|Handler).*?\)",
            RegexOptions.Singleline |
            RegexOptions.Compiled);



    private static readonly Regex DelegationRegex =
        new(
            @"\b_inner\.\w+\s*\(",
            RegexOptions.Compiled);



    public DecoratorPatternEvaluator(
        ILogger<DecoratorPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Architecture.DesignPatterns
            ?? new();
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



        if (!_policy.EnforceDecoratorPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Decorator pattern evaluation disabled by policy.");

            return results;
        }



        var files =
            ResolveSourceFiles(
                projectPath,
                ResolveExtensions());



        if (files.Count == 0)
            return results;



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Scanning {files.Count} files for decorator pattern.");



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
                AegisDiagnostics.Report(
                    Name,
                    DiagnosticLevel.Warning,
                    $"Unable to read {file}.",
                    ex);

                continue;
            }



            var metrics =
                AnalyzeDecorator(
                    content);



            if (!metrics.IsCandidate)
                continue;



            results.Add(
                CreateResult(
                    file,
                    metrics));
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
            $"Decorator evaluation completed with {results.Count} entries.");



        return results;
    }



    private string[] ResolveExtensions()
    {
        return Context?.Language switch
        {
            "C#" =>
            [
                ".cs"
            ],

            "Java" =>
            [
                ".java"
            ],

            "TypeScript" =>
            [
                ".ts"
            ],

            _ =>
            [
                ".cs",
                ".java",
                ".ts"
            ]
        };
    }



    private static DecoratorMetrics AnalyzeDecorator(
        string content)
    {
        var interfaceImplementation =
            InterfaceImplementationRegex.IsMatch(content);



        var innerComponent =
            InnerComponentRegex.IsMatch(content);



        var constructorInjection =
            ConstructorInjectionRegex.IsMatch(content);



        var delegation =
            DelegationRegex.IsMatch(content);



        if (!innerComponent ||
            !interfaceImplementation)
        {
            return DecoratorMetrics.Empty;
        }



        var score =
            Math.Round(
                (
                    (constructorInjection ? 1 : 0)
                    +
                    (delegation ? 1 : 0)
                )
                /
                2d
                *
                100,
                2);



        return new DecoratorMetrics
        {
            IsCandidate = true,

            HasInterfaceImplementation =
                interfaceImplementation,

            HasInnerComponent =
                innerComponent,

            HasConstructorInjection =
                constructorInjection,

            HasDelegationCalls =
                delegation,

            ComplianceScore =
                score
        };
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        DecoratorMetrics metrics)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            ProjectName =
                Context?.ProjectName,

            Language =
                Context?.Language,

            Framework =
                Context?.Framework,

            Layer =
                ResolveLayer(file),

            DetectionConfidence =
                Context?.Confidence ?? 0,


            Category =
                nameof(
                    ArchitectureRuleCategory.DesignPatterns),


            Metrics =
            {
                ["HasInterfaceImplementation"] =
                    metrics.HasInterfaceImplementation ? 1 : 0,

                ["HasInnerComponent"] =
                    metrics.HasInnerComponent ? 1 : 0,

                ["HasConstructorInjection"] =
                    metrics.HasConstructorInjection ? 1 : 0,

                ["HasDelegationCalls"] =
                    metrics.HasDelegationCalls ? 1 : 0,

                ["DecoratorComplianceScore"] =
                    metrics.ComplianceScore
            },


            Metadata =
            {
                ["Language"] =
                    Context?.Language
                    ?? "Unknown",

                ["Framework"] =
                    Context?.Framework
                    ?? "Unknown",

                ["ArchitectureStyle"] =
                    Context?.ArchitectureStyle
                    ?? "Unknown",

                ["Layer"] =
                    ResolveLayer(file)
                    ?? "Unknown"
            }
        };
    }



    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        IEnumerable<ArchitectureEvaluatorResult> results)
    {
        var list =
            results.ToList();



        return new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            ProjectName =
                Context?.ProjectName,

            Category =
                "DesignPatternSummary",


            Metrics =
            {
                ["DecoratorCount"] =
                    list.Count,

                ["AverageComplianceScore"] =
                    list.Average(
                        x =>
                            x.Metrics.GetValueOrDefault(
                                "DecoratorComplianceScore"))
            },


            Metadata =
            {
                ["ArchitectureStyle"] =
                    Context?.ArchitectureStyle
                    ?? "Unknown"
            }
        };
    }



    private string? ResolveLayer(
        string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(
                layer =>
                    layer.Files.Contains(
                        file,
                        StringComparer.OrdinalIgnoreCase))
            ?.Name;
    }



    private sealed class DecoratorMetrics
    {
        public static DecoratorMetrics Empty =>
            new();



        public bool IsCandidate { get; init; }

        public bool HasInterfaceImplementation { get; init; }

        public bool HasInnerComponent { get; init; }

        public bool HasConstructorInjection { get; init; }

        public bool HasDelegationCalls { get; init; }

        public double ComplianceScore { get; init; }
    }
}