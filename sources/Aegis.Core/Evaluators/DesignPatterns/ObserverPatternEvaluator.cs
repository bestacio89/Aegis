using System.Text.RegularExpressions;

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

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Evaluates observer and event-driven communication signals.
///
/// Detects:
/// - Observer, subscriber, and listener declarations
/// - Event handler and consumer contracts
/// - Event publication and subscription calls
/// - Subscription lifecycle management signals
/// - Potential polling-based event detection
/// - Event infrastructure indicators
///
/// Detection is lexical and heuristic. It does not prove semantic
/// implementation, correct event delivery, or leak-free lifecycle management.
/// </summary>
public sealed class ObserverPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "ObserverPatternEvaluator";

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

    private static readonly Regex StringLiteralRegex = new(
        """
        @(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`
        """,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommentRegex = new(
        @"//[^\r\n]*|/\*[\s\S]*?\*/|#[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ObserverRegex = new(
        @"\bclass\s+\w+(?:Observer|Subscriber|Listener)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex HandlerRegex = new(
        @"\bI?(?:Notification|Event)(?:Handler|Consumer)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex PublishRegex = new(
        @"\b(?:Publish|Send|Dispatch|Emit|Raise)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex SubscribeRegex = new(
        @"\b(?:Subscribe|Attach|Register)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex DisposeRegex = new(
        @"\b(?:Unsubscribe|Detach|Dispose|RemoveHandler)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex PollingRegex = new(
        @"\bwhile\s*\([^)]*\b(?:Check|Get|Has)(?:Status|State|Update|Event)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

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
        ArgumentNullException.ThrowIfNull(options);

        _policy =
            options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceObserverPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Observer evaluation disabled by policy.");

            return results;
        }

        var files = ResolveSourceFiles(
            projectPath,
            ".cs",
            ".java",
            ".py",
            ".ts");

        var contents = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        var unreadableFiles = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                contents[file] = await File.ReadAllTextAsync(
                    file,
                    token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    ex,
                    "{Evaluator} could not read source file {FilePath}.",
                    Name,
                    file);
            }
            catch (UnauthorizedAccessException ex)
            {
                unreadableFiles++;

                _logger.LogWarning(
                    ex,
                    "{Evaluator} has no access to source file {FilePath}.",
                    Name,
                    file);
            }
        }

        var projectEventFramework =
            DetectEventFramework(contents.Values);

        var analyzedComponents = 0;
        var componentsWithLeakRisk = 0;
        var componentsWithPolling = 0;
        var complianceScores = new List<double>();

        foreach (var entry in contents)
        {
            token.ThrowIfCancellationRequested();

            var source = RemoveCommentsAndStrings(entry.Value);

            var isObserver = ObserverRegex.IsMatch(source);
            var hasHandler = HandlerRegex.IsMatch(source);

            var publishCount = PublishRegex.Matches(source).Count;
            var subscribeCount = SubscribeRegex.Matches(source).Count;

            var hasLifecycle = DisposeRegex.IsMatch(source);
            var polling = PollingRegex.IsMatch(source);

            // Framework presence elsewhere in the project must not make
            // this file appear event-driven.
            var fileEventFramework = DetectEventFramework([entry.Value]);

            var eventDriven =
                isObserver ||
                hasHandler ||
                publishCount > 0 ||
                subscribeCount > 0 ||
                fileEventFramework != "Unknown";

            if (!isObserver &&
                !hasHandler &&
                publishCount == 0 &&
                subscribeCount == 0 &&
                !polling &&
                fileEventFramework == "Unknown")
            {
                continue;
            }

            analyzedComponents++;

            // This is a heuristic signal, not proof of a memory leak.
            var leakRisk =
                subscribeCount > 0 && !hasLifecycle ? 1d : 0d;

            if (leakRisk > 0)
            {
                componentsWithLeakRisk++;
            }

            if (polling)
            {
                componentsWithPolling++;
            }

            var maxSubscribers = Math.Max(0, _policy.MaxSubscribers);

            // A subscription count is not inherently non-compliant.
            // Flag only a count that exceeds the configured limit.
            var subscriberLimitExceeded =
                subscribeCount > maxSubscribers;

            var eventAlignment = eventDriven ? 1d : 0d;

            // No architectural Context is available here, so hexagonal
            // alignment cannot be evaluated honestly by this evaluator.
            // Do not invent an alignment score or include it in compliance.
            var score = ComputeCompliance(
                eventAlignment,
                leakRisk,
                subscriberLimitExceeded,
                polling);

            complianceScores.Add(score);

            var result = new ArchitectureEvaluatorResult(
                Name,
                entry.Key)
            {
                Category =
                    ArchitectureRuleCategory.DesignPatterns.ToString()
            };

            result.Metrics["IsObserver"] = isObserver ? 1 : 0;
            result.Metrics["IsEventHandler"] = hasHandler ? 1 : 0;
            result.Metrics["PublishCount"] = publishCount;
            result.Metrics["SubscriptionCount"] = subscribeCount;
            result.Metrics["LifecycleManaged"] = hasLifecycle ? 1 : 0;
            result.Metrics["LeakRisk"] = leakRisk;
            result.Metrics["PollingPenalty"] = polling ? 1 : 0;
            result.Metrics["EventDrivenAlignment"] = eventAlignment;
            result.Metrics["SubscriberLimitExceeded"] =
                subscriberLimitExceeded ? 1 : 0;
            result.Metrics["ObserverComplianceScore"] = score;

            result.Metadata[BaseArchitectureEvaluator.FilePathMetadataKey] =
                entry.Key;
            result.Metadata[BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
                BaseArchitectureEvaluator.FileScope;
            result.Metadata["FileName"] = Path.GetFileName(entry.Key);
            result.Metadata["Language"] = DetectLanguage(entry.Key);
            result.Metadata["EventFramework"] = fileEventFramework;
            result.Metadata["HexagonalAlignmentEvaluated"] = bool.FalseString;
            result.Metadata["SemanticAnalysisPerformed"] = bool.FalseString;
            result.Metadata["LifecycleVerified"] = bool.FalseString;

            results.Add(result);
        }

        var averageCompliance = complianceScores.Count > 0
            ? complianceScores.Average()
            : 0d;

        var leakRate = analyzedComponents > 0
            ? componentsWithLeakRisk / (double)analyzedComponents
            : 0d;

        var overallHealth = complianceScores.Count > 0
            ? averageCompliance * (1d - leakRate)
            : 0d;

        var summary = new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            Category =
                ArchitectureRuleCategory.DesignPatterns.ToString()
        };

        summary.Metrics["DiscoveredFiles"] = files.Count;
        summary.Metrics["AnalyzedFiles"] = contents.Count;
        summary.Metrics["UnreadableFiles"] = unreadableFiles;
        summary.Metrics["AnalyzedComponents"] = analyzedComponents;
        summary.Metrics["AverageComplianceScore"] = averageCompliance;
        summary.Metrics["ComponentsWithLeakRisk"] = componentsWithLeakRisk;
        summary.Metrics["ComponentLeakRiskRate"] = leakRate;
        summary.Metrics["ComponentsWithPollingSignals"] = componentsWithPolling;
        summary.Metrics["OverallObserverHealth"] = overallHealth;

        summary.Metadata[BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
            BaseArchitectureEvaluator.ProjectScope;
        summary.Metadata["Evaluator"] = Name;
        summary.Metadata["EventFramework"] = projectEventFramework;
        summary.Metadata["SemanticAnalysisPerformed"] = bool.FalseString;
        summary.Metadata["HexagonalAlignmentEvaluated"] = bool.FalseString;

        results.Add(summary);

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Observer evaluation completed: {files.Count} discovered files, " +
            $"{contents.Count} readable files, {analyzedComponents} detected " +
            $"components, {unreadableFiles} unreadable files.");

        return results;
    }

    private static string RemoveCommentsAndStrings(string source)
    {
        // Mask literals before comments so comment-like characters inside
        // strings are not mistaken for actual comments.
        var withoutStrings = StringLiteralRegex.Replace(
            source,
            match => new string(' ', match.Length));

        return CommentRegex.Replace(
            withoutStrings,
            match => new string(' ', match.Length));
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

    private static string DetectLanguage(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            ".ts" => "TypeScript",
            _ => "Unknown"
        };

    private static double ComputeCompliance(
        double eventAlignment,
        double leakRisk,
        bool subscriberLimitExceeded,
        bool polling)
    {
        // Hexagonal alignment is intentionally excluded because this
        // evaluator has no reliable architectural-style context.
        var score =
            eventAlignment * 0.35 +
            (1d - leakRisk) * 0.30 +
            (subscriberLimitExceeded ? 0d : 1d) * 0.15 +
            (polling ? 0d : 1d) * 0.20;

        return Math.Round(score * 100d, 2);
    }
}