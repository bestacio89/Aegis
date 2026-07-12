using System.Text.RegularExpressions;

using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;


/// <summary>
/// Evaluates Mediator pattern adoption and communication discipline.
///
/// Detects:
/// - Mediator abstraction usage
/// - Handler implementation density
/// - Direct service coupling
/// - Command/query handler adherence
/// - Hexagonal architecture communication boundaries
/// - Microservice communication discipline
///
/// Produces deterministic metrics consumed by RuleEngine.
/// </summary>
public sealed class MediatorPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;



    public override string Name =>
        "MediatorPatternEvaluator";



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
        "NestJS",
        "Angular",
        "FastAPI",
        "CleanArchitecture",
        "Hexagonal",
        "DDD",
        "Microservices"
    ];



    private static readonly Regex HandlerRegex =
        new(
            @"class\s+\w+(Handler)\b",
            RegexOptions.Compiled);



    private static readonly Regex DirectCouplingRegex =
        new(
            @"\b(new\s+|await\s+)?\w+(Service|Repository|Client|Handler)\s*\.\w+\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex MediatorHandlerRegex =
        new(
            @"I(Request|Command|Query|Notification)Handler",
            RegexOptions.Compiled);



    private static readonly Regex PortRegex =
        new(
            @"I\w+(Service|Repository|Client|Gateway|Port)\b",
            RegexOptions.Compiled);



    public MediatorPatternEvaluator(
        ILogger<MediatorPatternEvaluator> logger,
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


        var language =
            Context?.Language
            ?? "Unknown";



        if (_policy.MediatorApplicableLanguages.TryGetValue(
                language,
                out var enabled)
            && !enabled)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                $"Mediator evaluation disabled for {language}.");

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
            catch
            {
                continue;
            }
        }



        var mediatorFramework =
            DetectMediatorFramework(
                contents.Values);



        foreach (var entry in contents)
        {
            token.ThrowIfCancellationRequested();


            var content =
                entry.Value;



            var handlerCount =
                HandlerRegex.Matches(content).Count;



            var mediatorCalls =
                CountMediatorCalls(content);



            var hasMediatorContract =
                MediatorHandlerRegex.IsMatch(content);



            var directCoupling =
                DirectCouplingRegex.IsMatch(content);



            var usesPorts =
                PortRegex.IsMatch(content);



            var handlerDensity =
                handlerCount /
                (double)Math.Max(
                    1,
                    _policy.MaxHandlersPerFile);



            var mediatorRatio =
                Math.Min(
                    mediatorCalls /
                    (double)Math.Max(
                        1,
                        _policy.MaxMediatorCallsPerFile),
                    1);



            var couplingRisk =
                directCoupling
                    ? 1d
                    : 0d;



            var hexagonalAlignment =
                usesPorts
                    ? 1d
                    : 0d;



            var compliance =
                ComputeCompliance(
                    hasMediatorContract ? 1 : 0,
                    mediatorRatio,
                    couplingRisk,
                    handlerDensity,
                    hexagonalAlignment);



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
                        ["HandlerCount"] =
                            handlerCount,

                        ["MediatorCallCount"] =
                            mediatorCalls,

                        ["UsesMediator"] =
                            mediatorCalls > 0
                                ? 1
                                : 0,

                        ["HandlerDensity"] =
                            handlerDensity,

                        ["MediatorUsageRatio"] =
                            mediatorRatio,

                        ["CouplingRisk"] =
                            couplingRisk,

                        ["HexagonalPortAlignment"] =
                            hexagonalAlignment,

                        ["MediatorComplianceScore"] =
                            compliance
                    },


                    Metadata =
                    {
                        ["Framework"] =
                            mediatorFramework,

                        ["Language"] =
                            language,

                        ["Layer"] =
                            Context?.Layer
                            ?? "Unknown",

                        ["ArchitectureStyle"] =
                            Context?.ArchitectureStyle
                            ?? "Unknown",

                        ["FileName"] =
                            Path.GetFileName(entry.Key)
                    }
                });
        }



        if (results.Count > 0)
        {
            var analyzed =
                results.ToList();



            var average =
                analyzed.Average(
                    x =>
                        x.Metrics.GetValueOrDefault(
                            "MediatorComplianceScore"));



            var couplingViolations =
                analyzed.Count(
                    x =>
                        x.Metrics.GetValueOrDefault(
                            "CouplingRisk") > 0);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    projectPath)
                {
                    Category =
                        "DesignPatternSummary",


                    Metrics =
                    {
                        ["AnalyzedFiles"] =
                            analyzed.Count,

                        ["AverageComplianceScore"] =
                            average,

                        ["CouplingViolations"] =
                            couplingViolations,

                        ["OverallMediatorHealth"] =
                            average *
                            (
                                1 -
                                couplingViolations /
                                (double)Math.Max(
                                    1,
                                    analyzed.Count)
                            )
                    },


                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["MediatorFramework"] =
                            mediatorFramework
                    }
                });
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Mediator evaluation completed with {results.Count} entries.");



        return results;
    }



    private int CountMediatorCalls(
        string content)
    {
        return _policy.MediatorMethodHints.Sum(
            hint =>
                Regex.Matches(
                    content,
                    $@"\b{hint}\s*\(",
                    RegexOptions.IgnoreCase)
                .Count);
    }



    private static string DetectMediatorFramework(
        IEnumerable<string> contents)
    {
        foreach (var content in contents)
        {
            if (content.Contains(
                    "Franz.Common.Mediator",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "FranzMediator";
            }


            if (content.Contains(
                    "MediatR",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "MediatR";
            }
        }


        return "GenericMediator";
    }



    private static double ComputeCompliance(
        double contract,
        double usage,
        double coupling,
        double density,
        double hexagonal)
    {
        var score =
            contract * 0.25 +
            usage * 0.30 +
            (1 - coupling) * 0.20 +
            (1 - Math.Min(density, 1)) * 0.10 +
            hexagonal * 0.15;


        return Math.Round(
            score * 100,
            2);
    }
}