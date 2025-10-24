using Aegis.Core.Analysis;
using Aegis.Core.Architecture.Aggregation;
using Aegis.Core.Architecture.Scoring;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Aegis.Shared.Models.Rules;
using Microsoft.Extensions.Logging;

namespace Aegis.Core.Architecture.Services;

/// <summary>
/// 🧩 Coordinates the full deterministic Aegis pipeline:
/// Context → Evaluation → Weighting → Aggregation → Report.
/// </summary>
public sealed class AegisArchitectureAnalyzer
{
    private readonly IEnumerable<IEvaluator> _evaluators;
    private readonly CrossEvaluatorAggregator _aggregator;
    private readonly RuleWeightingEngine _weighting;
    private readonly ILogger<AegisArchitectureAnalyzer> _logger;

    public AegisArchitectureAnalyzer(
        IEnumerable<IEvaluator> evaluators,
        CrossEvaluatorAggregator aggregator,
        RuleWeightingEngine weighting,
        ILogger<AegisArchitectureAnalyzer> logger)
    {
        _evaluators = evaluators;
        _aggregator = aggregator;
        _weighting = weighting;
        _logger = logger;
    }

    public async Task<AegisArchitectureReport> AnalyzeAsync(string projectPath, CancellationToken token = default)
    {
        // 1) Detect project context
        _logger.LogInformation("🔍 Detecting project context for {Path}", projectPath);
        var context = ProjectContextDetector.Detect(projectPath);
        _logger.LogInformation("🧭 Detected context: {Lang}/{Framework} [{DomainType}/{Layer}] ({Architecture})",
            context.Language, context.Framework, context.DomainType, context.Layer, context.ArchitectureStyle);

        // 2) Run all evaluators
        var allResults = new List<ArchitectureEvaluatorResult>();
        foreach (var evaluator in _evaluators)
        {
            token.ThrowIfCancellationRequested();
            _logger.LogInformation("⚙️ Running evaluator: {Evaluator}", evaluator.GetType().Name);

            // IEvaluator signature: EvaluateAsync(string projectPath, ProjectContext context, CancellationToken)
            var results = await evaluator.EvaluateAsync(projectPath, context, token);

            foreach (var result in results)
            {
                // annotate only structural hints (no language/framework duplication here)
                result.Domain = context.DomainType ?? "General";
                result.Category ??= context.Layer ?? "Unknown";

                result.Metadata ??= new Dictionary<string, string>();
                result.Metadata["DomainType"] = context.DomainType ?? "Unknown";
                result.Metadata["Layer"] = context.Layer ?? "Unknown";
                result.Metadata["ArchitectureStyle"] = context.ArchitectureStyle ?? "Unknown";
                result.Metadata["Nature"] = context.Nature ?? "Generic";

                foreach (var rule in result.RuleResults)
                {
                    rule.Domain = context.DomainType ?? "General";
                }
            }

            allResults.AddRange(results);
        }

        // 3) Apply rule weighting (operates on RuleResult)
        _logger.LogInformation("⚖️ Applying rule weighting to rule results...");
        var allRuleResults = allResults.SelectMany(r => r.RuleResults);
        // ApplyWeights mutates/returns the same RuleResult instances with WeightedImpact set
        foreach (var _ in _weighting.ApplyWeights(allRuleResults)) { /* no-op: effects applied in place */ }

        // 4) Aggregate evaluator results into a comprehensive report
        _logger.LogInformation("📊 Aggregating evaluator results...");
        var report = _aggregator.Aggregate(allResults); // <- pass EvaluatorResults, not rules

        // 5) Embed project context + metadata snapshot
        report.ProjectPath = projectPath;
        report.ProjectName = Path.GetFileName(projectPath);
        report.Language = context.Language;
        report.Framework = context.Framework;
        report.TotalFilesScanned = context.FileCount;

        // add a non-rule context “fact” for dashboards/exports
        report.Facts.Add(BuildContextFact(context));

        // 6) Compute final compliance metrics
        report.ComputeCompliance();

        _logger.LogInformation("✅ Analysis complete for {Project}: {Lang}/{Framework} | Health={Health:0.00}%",
            report.ProjectName, report.Language, report.Framework, report.Metrics.ProjectHealthIndex);

        return report;
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private static ArchitectureEvaluatorResult BuildContextFact(ProjectArchitectureContext context)
    {
        var metadata = new Dictionary<string, string>
        {
            ["DomainType"] = context.DomainType ?? "Unknown",
            ["Layer"] = context.Layer ?? "Unknown",
            ["ArchitectureStyle"] = context.ArchitectureStyle ?? "Unknown",
            ["Nature"] = context.Nature ?? "Generic",
            ["BuildSystem"] = context.BuildSystem.ToString(),
            ["EntryPointFile"] = context.EntryPointFile ?? "",
            ["TargetRuntime"] = context.TargetRuntime ?? "Unknown",
            ["DetectorVersion"] = context.DetectorVersion,
            ["DetectionStrategy"] = context.DetectionStrategy,
            ["IsDeployable"] = context.IsDeployable.ToString(),
            ["Dockerized"] = context.IsDockerized.ToString(),
            ["UsesKubernetes"] = context.UsesKubernetes.ToString(),
            ["Confidence"] = context.Confidence.ToString("P0"),
            ["FileCount"] = context.FileCount.ToString(),
            ["LinesOfCode"] = context.LinesOfCode.ToString(),
            ["AverageComplexity"] = context.AverageComplexity.ToString("0.00")
        };

        if (context.Metadata is not null)
        {
            metadata["ProjectName"] = context.Metadata.Name;
            metadata["ProjectPath"] = context.Metadata.Path;
            metadata["ProjectFramework"] = context.Metadata.Framework;
            metadata["ProjectVersion"] = context.Metadata.Version;
            metadata["LastModified"] = context.Metadata.LastModified.ToString("u");
        }

        return new ArchitectureEvaluatorResult("ProjectContextFact", context.RootPath)
        {
            Domain = context.DomainType ?? "General",
            Metrics = new()
            {
                ["Confidence"] = context.Confidence,
                ["FileCount"] = context.FileCount,
                ["AvgComplexity"] = context.AverageComplexity
            },
            Metadata = metadata
        };
    }
}
