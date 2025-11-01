using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Enums;
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
/// Outputs a MediatorComplianceScore (0–100).
/// </summary>
public sealed class MediatorPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "MediatorPatternEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Java", "TypeScript", "Python"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring", "NestJS", "Angular", "FastAPI"];

    private static readonly Regex HandlerClassRx = new(@"class\s+(\w+Handler)\b", RegexOptions.Compiled);
    private static readonly Regex DirectServiceCallRx = new(@"\b(new\s+|await\s+)?\w+(Handler|Service|Command)\s*\.\w+\s*\(", RegexOptions.Compiled);
    private static readonly Regex MediatorInterfaceRx = new(@"I(Request|Command|Query|Notification)Handler", RegexOptions.Compiled);

    public MediatorPatternEvaluator(ILogger<MediatorPatternEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture?.DesignPatterns ?? new DesignPatternPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();
        var language = Context?.Language ?? "Unknown";

        // 1️⃣ Check if mediator analysis is enabled for the language
        if (!_policy.MediatorApplicableLanguages.TryGetValue(language, out var enabled) || !enabled)
        {
            _logger.LogInformation("🔇 Mediator analysis skipped for {Lang}", language);
            return results;
        }

        var framework = DetectMediatorFramework(projectPath);
        _logger.LogInformation("🧭 Detected mediator flavor: {Framework}", framework);

        var files = Directory.EnumerateFiles(projectPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcludedDir(f))
            .ToList();

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            string content;
            try { content = await File.ReadAllTextAsync(file, token); }
            catch { continue; }

            var fileName = Path.GetFileName(file);
            var handlerMatches = HandlerClassRx.Matches(content);
            int handlerCount = handlerMatches.Count;
            int mediatorCalls = _policy.MediatorMethodHints.Sum(m => Regex.Matches(content, m + @"\s*\(", RegexOptions.IgnoreCase).Count);
            bool hasMediatorInterface = MediatorInterfaceRx.IsMatch(content);
            bool hasDirectCoupling = DirectServiceCallRx.IsMatch(content);
            bool usesMediator =
                _policy.MediatorFrameworkHints
                    .Where(h => h.Key.Equals(framework, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(h => h.Value)
                    .Any(i => content.Contains(i, StringComparison.OrdinalIgnoreCase))
                || mediatorCalls > 0;

            // --- Derived ratios ---
            double handlerDensity = handlerCount / (double)Math.Max(1, _policy.MaxHandlersPerFile);
            double mediatorUsageRatio = Math.Min(mediatorCalls / (double)Math.Max(1, _policy.MaxMediatorCallsPerFile), 1.0);
            double couplingRisk = hasDirectCoupling ? 1.0 : 0.0;
            double interfaceAdherence = hasMediatorInterface ? 1.0 : 0.0;

            // --- Compliance scoring ---
            double complianceScore = ComputeCompliance(interfaceAdherence, mediatorUsageRatio, couplingRisk, handlerDensity);

            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "DesignPattern",
                Metrics = new Dictionary<string, double>
                {
                    ["HandlerCount"] = handlerCount,
                    ["MediatorCallCount"] = mediatorCalls,
                    ["HandlerDensity"] = handlerDensity,
                    ["MediatorUsageRatio"] = mediatorUsageRatio,
                    ["CouplingRisk"] = couplingRisk,
                    ["InterfaceAdherence"] = interfaceAdherence,
                    ["MediatorComplianceScore"] = complianceScore
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = fileName,
                    ["Framework"] = framework,
                    ["Language"] = language,
                    ["HasMediatorInterface"] = hasMediatorInterface.ToString(),
                    ["HasDirectCoupling"] = hasDirectCoupling.ToString(),
                    ["Policy_MaxHandlersPerFile"] = _policy.MaxHandlersPerFile.ToString(),
                    ["Policy_MaxMediatorCallsPerFile"] = _policy.MaxMediatorCallsPerFile.ToString()
                }
            });
        }

        // 🧩 Aggregated metrics
        if (results.Count > 0)
        {
            double avgCompliance = results.Average(r => r.Metrics.GetValueOrDefault("MediatorComplianceScore", 0));
            double avgHandlers = results.Average(r => r.Metrics.GetValueOrDefault("HandlerCount", 0));
            double avgMediatorCalls = results.Average(r => r.Metrics.GetValueOrDefault("MediatorCallCount", 0));
            double couplingCount = results.Count(r => r.Metrics.GetValueOrDefault("CouplingRisk", 0) == 1);

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "DesignPatternSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["FileCount"] = results.Count,
                    ["AverageComplianceScore"] = avgCompliance,
                    ["AverageHandlerCount"] = avgHandlers,
                    ["AverageMediatorCalls"] = avgMediatorCalls,
                    ["CoupledFiles"] = couplingCount,
                    ["OverallMediatorHealth"] = avgCompliance * (1 - couplingCount / Math.Max(1, results.Count))
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["DetectedFramework"] = framework
                }
            });
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} metric entries", Name, results.Count);
        return results;
    }

    private static double ComputeCompliance(double interfaceAdherence, double mediatorUsageRatio, double couplingRisk, double handlerDensity)
    {
        // Higher coupling & density reduce score, while adherence & usage increase it.
        double score =
            interfaceAdherence * 0.3 +
            mediatorUsageRatio * 0.4 +
            (1 - Math.Min(handlerDensity, 1)) * 0.15 +
            (1 - couplingRisk) * 0.15;

        return Math.Round(score * 100, 2);
    }

    private static string DetectMediatorFramework(string projectPath)
    {
        foreach (var file in Directory.EnumerateFiles(projectPath, "*.cs", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(file);
            if (content.Contains("Franz.Common.Mediator", StringComparison.OrdinalIgnoreCase))
                return "FranzMediator";
            if (content.Contains("MediatR", StringComparison.OrdinalIgnoreCase))
                return "MediatR";
        }
        return "Unknown";
    }
}
