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
/// Evaluates Observer/Event-driven communication discipline.
///
/// Detects:
/// - Observer/subscriber implementations
/// - Domain event handlers
/// - Event publisher usage
/// - Subscription lifecycle management
/// - Polling instead of notification patterns
/// - Hexagonal and microservice event alignment
///
/// Produces deterministic metrics consumed by RuleEngine.
/// </summary>
public sealed class ObserverPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
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
        "FastAPI",
        "DDD",
        "Hexagonal",
        "Microservices",
        "EventDriven"
    ];



    private static readonly Regex ObserverRegex =
        new(
            @"class\s+\w+(Observer|Subscriber|Listener)\b",
            RegexOptions.Compiled);



    private static readonly Regex HandlerRegex =
        new(
            @"I?(Notification|Event)(Handler|Consumer)\b",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex PublishRegex =
        new(
            @"\b(Publish|Send|Dispatch|Emit|Raise)\s*\(",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex SubscribeRegex =
        new(
            @"\b(Subscribe|Attach|Register)\s*\(",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex DisposeRegex =
        new(
            @"\b(Unsubscribe|Detach|Dispose|RemoveHandler)\s*\(",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex PollingRegex =
        new(
            @"while\s*\(.*(Check|Get|Has)(Status|State|Update|Event)",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly string[] EventFrameworkIndicators =
    [
        "Franz.Common.Messaging",
        "MediatR",
        "Kafka",
        "RabbitMQ",
        "EventBus",
        "IEventPublisher"
    ];



    public ObserverPatternEvaluator(
        ILogger<ObserverPatternEvaluator> logger,
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



        if (!_policy.EnforceObserverPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Observer evaluation disabled by policy.");

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
            }
        }



        var eventFramework =
            DetectEventFramework(
                contents.Values);



        foreach (var entry in contents)
        {
            token.ThrowIfCancellationRequested();


            var content =
                entry.Value;



            var isObserver =
                ObserverRegex.IsMatch(content);



            var hasHandler =
                HandlerRegex.IsMatch(content);



            var publishCount =
                PublishRegex.Matches(content).Count;



            var subscribeCount =
                SubscribeRegex.Matches(content).Count;



            var hasLifecycle =
                DisposeRegex.IsMatch(content);



            var polling =
                PollingRegex.IsMatch(content);



            var eventDriven =
                publishCount > 0 ||
                hasHandler ||
                eventFramework != "Unknown";



            if (!isObserver &&
                !hasHandler &&
                !eventDriven)
            {
                continue;
            }



            var leakRisk =
                subscribeCount > 0 &&
                !hasLifecycle
                    ? 1d
                    : 0d;



            var subscriptionRatio =
                Math.Min(
                    subscribeCount /
                    (double)Math.Max(
                        1,
                        _policy.MaxSubscribers),
                    1);



            var eventAlignment =
                eventDriven
                    ? 1d
                    : 0d;



            var hexagonalAlignment =
                Context?.Framework?.Contains(
                    "Hexagonal",
                    StringComparison.OrdinalIgnoreCase) == true
                    &&
                    eventDriven
                    ? 1d
                    : 0d;



            var score =
                ComputeCompliance(
                    eventAlignment,
                    hexagonalAlignment,
                    leakRisk,
                    subscriptionRatio,
                    polling ? 1 : 0);



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
                        ["IsObserver"] =
                            isObserver ? 1 : 0,

                        ["IsEventHandler"] =
                            hasHandler ? 1 : 0,

                        ["PublishCount"] =
                            publishCount,

                        ["SubscriptionCount"] =
                            subscribeCount,

                        ["LifecycleManaged"] =
                            hasLifecycle ? 1 : 0,

                        ["LeakRisk"] =
                            leakRisk,

                        ["PollingPenalty"] =
                            polling ? 1 : 0,

                        ["EventDrivenAlignment"] =
                            eventAlignment,

                        ["HexagonalAlignment"] =
                            hexagonalAlignment,

                        ["ObserverComplianceScore"] =
                            score
                    },


                    Metadata =
                    {
                        ["FilePath"] = entry.Key,

                        ["FileName"] =
                            Path.GetFileName(entry.Key),

                        ["Language"] =
                            Context?.Language
                            ?? "Unknown",

                        ["Framework"] =
                            Context?.Framework
                            ?? "Unknown",

                        ["EventFramework"] =
                            eventFramework
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
                            "ObserverComplianceScore"));



            var leaks =
                analyzed.Count(
                    x =>
                        x.Metrics.GetValueOrDefault(
                            "LeakRisk") > 0);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    projectPath)
                {
                    Category =
                        "DesignPatternSummary",


                    Metrics =
                    {
                        ["AnalyzedComponents"] =
                            analyzed.Count,

                        ["AverageComplianceScore"] =
                            average,

                        ["SubscriptionLeaks"] =
                            leaks,

                        ["OverallObserverHealth"] =
                            average *
                            (
                                1 -
                                leaks /
                                (double)Math.Max(
                                    1,
                                    analyzed.Count)
                            )
                    },


                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["EventFramework"] =
                            eventFramework
                    }
                });
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Observer evaluation completed with {results.Count} entries.");



        return results;
    }



    private static string DetectEventFramework(
        IEnumerable<string> contents)
    {
        foreach (var content in contents)
        {
            foreach (var indicator in EventFrameworkIndicators)
            {
                if (content.Contains(
                        indicator,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return indicator;
                }
            }
        }


        return "Unknown";
    }



    private static double ComputeCompliance(
        double eventAlignment,
        double hexagonalAlignment,
        double leakRisk,
        double subscriptionRatio,
        double pollingPenalty)
    {
        var score =
            eventAlignment * 0.30 +
            hexagonalAlignment * 0.20 +
            (1 - leakRisk) * 0.25 +
            (1 - subscriptionRatio) * 0.10 +
            (1 - pollingPenalty) * 0.15;


        return Math.Round(
            score * 100,
            2);
    }
}