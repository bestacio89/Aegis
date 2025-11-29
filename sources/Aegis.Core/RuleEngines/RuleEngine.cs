using Aegis.Architecture.Aggregation;
using Aegis.Architecture.Scoring;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using France.Common.Extensions;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.RuleEngines;

public sealed class RuleEngine
{
    private readonly IEnumerable<IEvaluator> _evaluators;
    private readonly IEnumerable<IReportExporter> _exporters;
    private readonly RuleEngineCore _core;
    private readonly RuleWeightingEngine _weighting;
    private readonly CrossEvaluatorAggregator _aggregator;
    private readonly ILogger<RuleEngine> _logger;

    private AegisArchitecturePolicy? _policy;

    public RuleEngine(
        IEnumerable<IEvaluator> evaluators,
        IEnumerable<IReportExporter> exporters,
        RuleEngineCore core,
        RuleWeightingEngine weighting,
        CrossEvaluatorAggregator aggregator,
        ILogger<RuleEngine> logger)
    {
        _evaluators = evaluators;
        _exporters = exporters;
        _core = core;
        _weighting = weighting;
        _aggregator = aggregator;
        _logger = logger;
    }

    // ==============================================================
    // 🧭 POLICY APPLICATION
    // ==============================================================

    /// <summary>
    /// Applies a loaded Aegis policy file to all evaluators and weighting engines.
    /// </summary>
    public void ApplyPolicy(AegisArchitecturePolicy policy)
    {
        _policy = policy;
        _logger.LogInformation("⚙️ Applying Aegis policy → {Version}", policy.Version ?? "1.0");

        foreach (var evaluator in _evaluators)
        {
            var name = evaluator.Name.ToLowerInvariant();

            // Deterministic name-based mapping between evaluator and policy section
            if (name.Contains("architecture"))
                ApplyEvaluatorConfig(evaluator, policy.Architecture);
            else if (name.Contains("naming"))
                ApplyEvaluatorConfig(evaluator, policy.Naming);
            else if (name.Contains("dependency"))
                ApplyEvaluatorConfig(evaluator, policy.Dependency);
            else if (name.Contains("security"))
                ApplyEvaluatorConfig(evaluator, policy.Security);
            else if (name.Contains("performance"))
                ApplyEvaluatorConfig(evaluator, policy.Performance);
            else if (name.Contains("persistence") || name.Contains("repository"))
                ApplyEvaluatorConfig(evaluator, policy.Repository);
            else if (name.Contains("frontend"))
                ApplyEvaluatorConfig(evaluator, policy.Frontend);
            else if (name.Contains("node"))
                ApplyEvaluatorConfig(evaluator, policy.Node);
            else if (name.Contains("complexity"))
                ApplyEvaluatorConfig(evaluator, policy.Complexity);
            else if (name.Contains("maintainability"))
                ApplyEvaluatorConfig(evaluator, policy.Maintainability);
            else if (name.Contains("cohesion"))
                ApplyEvaluatorConfig(evaluator, policy.Cohesion);
            else if (name.Contains("coupling"))
                ApplyEvaluatorConfig(evaluator, policy.Coupling);
            else if (name.Contains("logging"))
                ApplyEvaluatorConfig(evaluator, policy.Logging);
            else if (name.Contains("transaction"))
                ApplyEvaluatorConfig(evaluator, policy.Transaction);
            else
                _logger.LogDebug("⚪ No specific policy section matched for evaluator {Evaluator}", evaluator.Name);
        }

        // Apply broader rules to weighting/aggregation layers
        _weighting.ApplyPolicy(policy);
        _aggregator.ApplyPolicy(policy);

        _core.ApplyPolicy(policy);

        _logger.LogInformation("✅ Policy successfully distributed to evaluators and sub-engines.");
    }

    private void ApplyEvaluatorConfig(IEvaluator evaluator, object policySection)
    {
        // Generic reflection-based config reader (safe)
        var enabledProp = policySection.GetType().GetProperty("Enabled");
        var weightProp = policySection.GetType().GetProperty("Weight");

        bool? enabled = enabledProp?.GetValue(policySection) as bool?;
        double? weight = weightProp?.GetValue(policySection) as double?;

        if (enabled.HasValue)
        {
            evaluator.IsEnabled = enabled.Value;
            _logger.LogInformation("🔧 {Evaluator}: Enabled={Enabled}", evaluator.Name, evaluator.IsEnabled);
        }

        if (weight.HasValue && weight.Value > 0)
        {
            evaluator.WeightFactor = weight.Value;
            _logger.LogInformation("⚖️ {Evaluator}: Weight={Weight}", evaluator.Name, evaluator.WeightFactor);
        }
    }

    // ==============================================================
    // 🚀 MAIN ANALYSIS PIPELINE
    // ==============================================================

    public async Task<AegisArchitectureReport> RunAsync(
        string projectPath,
        ProjectArchitectureContext context,
        CancellationToken token = default)
    {
        var report = new AegisArchitectureReport
        {
            ProjectName = Path.GetFileName(projectPath),
            ProjectPath = projectPath,
            Language = context.Language,
            Framework = context.Framework,
            ScanDate = DateTimeOffset.UtcNow
        };

        _logger.LogInformation("🚀 Starting Aegis scan for {Project} [{Lang}/{Framework}]",
            report.ProjectName, context.Language, context.Framework);

        var allFacts = new List<ArchitectureEvaluatorResult>();

        // 1️⃣ Run only enabled evaluators
        foreach (var evaluator in _evaluators.Where(e => e.IsEnabled))
        {
            if (!evaluator.SupportedLanguages.Contains(context.Language) &&
                !evaluator.SupportedLanguages.Contains("*"))
                continue;

            _logger.LogInformation("🔍 Running evaluator: {Evaluator}", evaluator.Name);

            try
            {
                var results = await evaluator.EvaluateAsync(projectPath, context, token);
                allFacts.AddRange(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Evaluator {Name} failed.", evaluator.Name);
            }
        }

        // 2️⃣ Interpret evaluator facts into rule results
        var ruleResults = _core.Evaluate(context, allFacts).ToList();

        // 3️⃣ Apply weighting
        foreach (var _ in _weighting.ApplyWeights(ruleResults)) { }

        // 4️⃣ Aggregate and compute metrics
        report.Results.AddRange(ruleResults);
        report.Facts.AddRange(allFacts);
        report.TotalFilesScanned = context.FileCount;
        report.ComputeCompliance();

        _logger.LogInformation("✅ Evaluation completed — {Count} rule violations detected.", ruleResults.Count);

        // 5️⃣ Export all report formats
        foreach (var exporter in _exporters)
        {
            try
            {
                var outputPath = Path.Combine(projectPath, $"AegisReport.{exporter.Format}");
                await exporter.ExportAsync(report, context, outputPath, ArchitectureReportDetailLevel.SummaryOnly, token);
                _logger.LogInformation("📝 {Format} report exported → {Path}", exporter.Format.ToUpper(), outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Report exporter {Format} failed.", exporter.Format);
            }
        }

        return report;
    }

    public IReportExporter? GetExporter(string format) =>
        _exporters.FirstOrDefault(e => e.Format.Equals(format, StringComparison.OrdinalIgnoreCase));
}
