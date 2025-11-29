using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Quantitatively evaluates Observer pattern usage and event discipline.
/// Detects subscription leaks, missing disposal, manual polling,
/// and interface non-compliance. Produces an ObserverComplianceScore (0–100).
/// </summary>
public sealed class ObserverPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "ObserverPatternEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Java", "Python", "TypeScript"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring", "RxJS", "FastAPI"];

    private static readonly Regex ObserverClassRx = new(@"class\s+(\w+Observer)\b", RegexOptions.Compiled);
    private static readonly Regex SubjectAttachRx = new(@"\b(Attach|Subscribe)\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SubjectDetachRx = new(@"\b(Detach|Unsubscribe|Dispose)\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex InterfaceRx = new(@"I?(Observer|Subscriber|Listener)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ManualPollingRx = new(@"\bwhile\s*\(.*\.has(Update|Change|Event)\(\)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EventCountRx = new(@"\.Subscribe\s*\(", RegexOptions.Compiled);

    public ObserverPatternEvaluator(ILogger<ObserverPatternEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns ?? new();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceObserverPattern)
        {
            _logger.LogInformation("🔕 Observer pattern enforcement disabled by policy.");
            return results;
        }

        var extensions = Context?.Language switch
        {
            "CSharp" => new[] { ".cs" },
            "Java" => new[] { ".java" },
            "Python" => new[] { ".py" },
            "TypeScript" => new[] { ".ts" },
            _ => new[] { ".cs", ".java", ".py", ".ts" }
        };

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogInformation("🔔 No files found for Observer pattern evaluation.");
            return results;
        }

        _logger.LogTrace("🔍 Scanning {Count} files for Observer pattern compliance...", files.Count);

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token);
            var fileName = Path.GetFileName(file);

            bool isObserver = ObserverClassRx.IsMatch(content);
            bool hasInterface = InterfaceRx.IsMatch(content);
            bool subscribes = SubjectAttachRx.IsMatch(content);
            bool unsubscribes = SubjectDetachRx.IsMatch(content);
            bool polling = ManualPollingRx.IsMatch(content);
            int subscriptionCount = EventCountRx.Matches(content).Count;

            // --- Derived metrics ---
            double leakRisk = subscribes && !unsubscribes ? 1.0 : 0.0;
            double subscriptionDensity = subscriptionCount / (double)Math.Max(1, _policy.MaxSubscribers);
            double interfaceAdherence = hasInterface ? 1.0 : 0.0;
            double pollingPenalty = polling ? 1.0 : 0.0;

            // --- Compliance scoring ---
            double complianceScore = ComputeCompliance(interfaceAdherence, leakRisk, subscriptionDensity, pollingPenalty);

            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "DesignPattern",
                Metrics = new Dictionary<string, double>
                {
                    ["IsObserverClass"] = isObserver ? 1 : 0,
                    ["LeakRisk"] = leakRisk,
                    ["SubscriptionDensity"] = subscriptionDensity,
                    ["InterfaceAdherence"] = interfaceAdherence,
                    ["PollingPenalty"] = pollingPenalty,
                    ["ObserverComplianceScore"] = complianceScore
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = fileName,
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown",
                    ["SubscriptionCount"] = subscriptionCount.ToString(),
                    ["RequireObserverInterface"] = _policy.RequireObserverInterface.ToString(),
                    ["MaxSubscribers"] = _policy.MaxSubscribers.ToString(),
                    ["DetectLeakingSubscriptions"] = _policy.DetectLeakingSubscriptions.ToString()
                }
            });
        }

        // 🔢 Aggregated summary
        if (results.Count > 0)
        {
            double avgScore = results.Average(r => r.Metrics.GetValueOrDefault("ObserverComplianceScore", 0));
            double avgDensity = results.Average(r => r.Metrics.GetValueOrDefault("SubscriptionDensity", 0));
            double leakCount = results.Count(r => r.Metrics.GetValueOrDefault("LeakRisk", 0) == 1);
            double pollingCount = results.Count(r => r.Metrics.GetValueOrDefault("PollingPenalty", 0) == 1);

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "DesignPatternSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["FileCount"] = results.Count,
                    ["AverageComplianceScore"] = avgScore,
                    ["AverageSubscriptionDensity"] = avgDensity,
                    ["LeakCount"] = leakCount,
                    ["PollingCount"] = pollingCount,
                    ["OverallObserverHealth"] = avgScore * (1 - (leakCount + pollingCount) / Math.Max(1.0, results.Count))
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.EnforceObserverPattern.ToString()
                }
            });
        }

        _logger.LogInformation("🔔 {Evaluator} completed with {Count} metric entries", Name, results.Count);
        return results;
    }

    private static double ComputeCompliance(double interfaceAdherence, double leakRisk, double subscriptionDensity, double pollingPenalty)
    {
        // Weighted scoring model:
        // Adherence & balance increase compliance; leaks/polling reduce it.
        double score =
            interfaceAdherence * 0.35 +
            (1 - Math.Min(subscriptionDensity, 1)) * 0.25 +
            (1 - leakRisk) * 0.25 +
            (1 - pollingPenalty) * 0.15;

        return Math.Round(score * 100, 2);
    }
}
