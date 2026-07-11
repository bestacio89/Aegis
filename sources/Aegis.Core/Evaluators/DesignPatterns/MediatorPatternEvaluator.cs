using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Quantitatively evaluates Mediator pattern adoption and communication discipline.
/// Detects and scores:
/// - Direct coupling vs mediated dispatch
/// - Interface adherence
/// - Handler density
/// - Over-dispatching ("God Mediator")
/// Outputs a MediatorComplianceScore (0-100).
/// </summary>
public sealed class MediatorPatternEvaluator : BaseArchitectureEvaluator
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
        "FastAPI"
    ];



    private static readonly Regex HandlerClassRegex =
        new(
            @"class\s+(\w+Handler)\b",
            RegexOptions.Compiled);



    private static readonly Regex DirectServiceCallRegex =
        new(
            @"\b(new\s+|await\s+)?\w+(Handler|Service|Command)\s*\.\w+\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex MediatorInterfaceRegex =
        new(
            @"I(Request|Command|Query|Notification)Handler",
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



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();


        var language =
            Context?.Language
            ?? "Unknown";



        if (!_policy.MediatorApplicableLanguages.TryGetValue(
                language,
                out var enabled)
            || !enabled)
        {
            _logger.LogInformation(
                "Mediator evaluation skipped for language {Language}",
                language);

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



        var framework =
            DetectMediatorFramework(files);



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



            var handlerCount =
                HandlerClassRegex.Matches(content).Count;



            var mediatorCalls =
                _policy.MediatorMethodHints.Sum(
                    method =>
                        Regex.Matches(
                            content,
                            method + @"\s*\(",
                            RegexOptions.IgnoreCase)
                        .Count);



            var hasMediatorInterface =
                MediatorInterfaceRegex.IsMatch(content);



            var hasDirectCoupling =
                DirectServiceCallRegex.IsMatch(content);



            var usesMediator =
                mediatorCalls > 0
                ||
                _policy.MediatorFrameworkHints
                    .Where(
                        h =>
                            h.Key.Equals(
                                framework,
                                StringComparison.OrdinalIgnoreCase))
                    .SelectMany(h => h.Value)
                    .Any(
                        hint =>
                            content.Contains(
                                hint,
                                StringComparison.OrdinalIgnoreCase));



            var handlerDensity =
                handlerCount /
                (double)Math.Max(
                    1,
                    _policy.MaxHandlersPerFile);



            var mediatorUsageRatio =
                mediatorCalls /
                (double)Math.Max(
                    1,
                    _policy.MaxMediatorCallsPerFile);



            mediatorUsageRatio =
                Math.Min(
                    mediatorUsageRatio,
                    1);



            var couplingRisk =
                hasDirectCoupling
                    ? 1
                    : 0;



            var interfaceAdherence =
                hasMediatorInterface
                    ? 1
                    : 0;



            var compliance =
                ComputeCompliance(
                    interfaceAdherence,
                    mediatorUsageRatio,
                    couplingRisk,
                    handlerDensity);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    file)
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
                            usesMediator
                                ? 1
                                : 0,

                        ["HandlerDensity"] =
                            handlerDensity,

                        ["MediatorUsageRatio"] =
                            mediatorUsageRatio,

                        ["CouplingRisk"] =
                            couplingRisk,

                        ["InterfaceAdherence"] =
                            interfaceAdherence,

                        ["MediatorComplianceScore"] =
                            compliance
                    },

                    Metadata =
                    {
                        ["Framework"] =
                            framework,

                        ["Language"] =
                            language,

                        ["FileName"] =
                            Path.GetFileName(file),

                        ["Layer"] =
                            Context?.Layer
                            ?? "Unknown"
                    }
                });
        }



        if (results.Count > 0)
        {
            var averageCompliance =
                results.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "MediatorComplianceScore",
                            0));



            var coupledFiles =
                results.Count(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "CouplingRisk",
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
                        ["AnalyzedFiles"] =
                            results.Count,

                        ["AverageComplianceScore"] =
                            averageCompliance,

                        ["CoupledFiles"] =
                            coupledFiles,

                        ["OverallMediatorHealth"] =
                            averageCompliance *
                            (
                                1 -
                                coupledFiles /
                                (double)Math.Max(
                                    1,
                                    results.Count)
                            )
                    },

                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["DetectedFramework"] =
                            framework
                    }
                });
        }



        _logger.LogInformation(
            "Mediator pattern evaluation completed with {Count} entries",
            results.Count);


        return results;
    }



    private static double ComputeCompliance(
        double interfaceAdherence,
        double mediatorUsageRatio,
        double couplingRisk,
        double handlerDensity)
    {
        var score =
            interfaceAdherence * 0.3 +
            mediatorUsageRatio * 0.4 +
            (1 - Math.Min(handlerDensity, 1)) * 0.15 +
            (1 - couplingRisk) * 0.15;


        return Math.Round(
            score * 100,
            2);
    }



    private static string DetectMediatorFramework(
        IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var content =
                File.ReadAllText(file);


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


        return "Unknown";
    }
}