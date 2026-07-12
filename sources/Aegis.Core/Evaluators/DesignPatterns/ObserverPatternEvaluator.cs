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
/// Quantitatively evaluates Observer pattern usage and event discipline.
/// Detects:
/// - Observer implementation
/// - Subscription lifecycle handling
/// - Missing unsubscribe/disposal patterns
/// - Manual polling instead of notifications
/// Produces an ObserverComplianceScore (0-100).
/// </summary>
public sealed class ObserverPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;


    public override string Name =>
        "ObserverPatternEvaluator";


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
        "RxJS",
        "FastAPI"
    ];



    private static readonly Regex ObserverClassRegex =
        new(
            @"class\s+(\w+Observer)\b",
            RegexOptions.Compiled);



    private static readonly Regex SubjectAttachRegex =
        new(
            @"\b(Attach|Subscribe)\s*\(",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex SubjectDetachRegex =
        new(
            @"\b(Detach|Unsubscribe|Dispose)\s*\(",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex InterfaceRegex =
        new(
            @"I?(Observer|Subscriber|Listener)\b",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex ManualPollingRegex =
        new(
            @"\bwhile\s*\(.*\.has(Update|Change|Event)\(\)\)",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    public ObserverPatternEvaluator(
        ILogger<ObserverPatternEvaluator> logger,
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



        if (!_policy.EnforceObserverPattern)
        {
            _logger.LogInformation(
                "Observer pattern evaluation disabled by policy.");

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



            var isObserver =
                ObserverClassRegex.IsMatch(content);



            var hasInterface =
                InterfaceRegex.IsMatch(content);



            var subscribes =
                SubjectAttachRegex.IsMatch(content);



            var unsubscribes =
                SubjectDetachRegex.IsMatch(content);



            var polling =
                ManualPollingRegex.IsMatch(content);



            var subscriptionCount =
                SubjectAttachRegex.Matches(content).Count;



            var leakRisk =
                subscribes && !unsubscribes
                    ? 1
                    : 0;



            var subscriptionDensity =
                subscriptionCount /
                (double)Math.Max(
                    1,
                    _policy.MaxSubscribers);



            subscriptionDensity =
                Math.Min(
                    subscriptionDensity,
                    1);



            var interfaceAdherence =
                hasInterface
                    ? 1
                    : 0;



            var pollingPenalty =
                polling
                    ? 1
                    : 0;



            var complianceScore =
                ComputeCompliance(
                    interfaceAdherence,
                    leakRisk,
                    subscriptionDensity,
                    pollingPenalty);



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
                        ["IsObserverClass"] =
                            isObserver
                                ? 1
                                : 0,

                        ["SubscriptionCount"] =
                            subscriptionCount,

                        ["LeakRisk"] =
                            leakRisk,

                        ["SubscriptionDensity"] =
                            subscriptionDensity,

                        ["InterfaceAdherence"] =
                            interfaceAdherence,

                        ["PollingPenalty"] =
                            pollingPenalty,

                        ["ObserverComplianceScore"] =
                            complianceScore
                    },

                    Metadata =
                    {
                        ["FileName"] =
                            Path.GetFileName(file),

                        ["Language"] =
                            Context?.Language
                            ?? "Unknown",

                        ["Framework"] =
                            Context?.Framework
                            ?? "Unknown",

                        ["Layer"] =
                            Context?.Layer
                            ?? "Unknown",

                        ["RequireObserverInterface"] =
                            _policy.RequireObserverInterface.ToString(),

                        ["DetectLeakingSubscriptions"] =
                            _policy.DetectLeakingSubscriptions.ToString()
                    }
                });
        }



        if (results.Count > 0)
        {
            var observerCount =
                results.Count;



            var avgScore =
                results.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "ObserverComplianceScore",
                            0));



            var avgDensity =
                results.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "SubscriptionDensity",
                            0));



            var leakCount =
                results.Count(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "LeakRisk",
                            0) > 0);



            var pollingCount =
                results.Count(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "PollingPenalty",
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
                        ["ObserverCount"] =
                            observerCount,

                        ["AverageComplianceScore"] =
                            avgScore,

                        ["AverageSubscriptionDensity"] =
                            avgDensity,

                        ["LeakCount"] =
                            leakCount,

                        ["PollingCount"] =
                            pollingCount,

                        ["OverallObserverHealth"] =
                            avgScore *
                            (
                                1 -
                                (leakCount + pollingCount) /
                                (double)Math.Max(
                                    1,
                                    observerCount)
                            )
                    },

                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["PolicyEnabled"] =
                            _policy.EnforceObserverPattern.ToString()
                    }
                });
        }



        _logger.LogInformation(
            "{Evaluator} completed with {Count} metric entries",
            Name,
            results.Count);



        return results;
    }



    private static double ComputeCompliance(
        double interfaceAdherence,
        double leakRisk,
        double subscriptionDensity,
        double pollingPenalty)
    {
        var score =
            interfaceAdherence * 0.35 +
            (1 - Math.Min(subscriptionDensity, 1)) * 0.25 +
            (1 - leakRisk) * 0.25 +
            (1 - pollingPenalty) * 0.15;


        return Math.Round(
            score * 100,
            2);
    }
}