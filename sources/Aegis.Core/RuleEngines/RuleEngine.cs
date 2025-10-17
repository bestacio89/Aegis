using Aegis.Shared.Models;
using France.Common.Extensions;
using Microsoft.Extensions.Logging;
using Aegis.Shared.Models.Rules;
using Aegis.Shared.Contracts;

namespace Aegis.Core.RuleEngines;

/// <summary>
/// 🚀 Main orchestrator of the Aegis analysis lifecycle.
/// 1️⃣ Runs evaluators to gather metrics.
/// 2️⃣ Invokes RuleEngineCore to interpret metrics into violations.
/// 3️⃣ Exports unified governance reports.
/// </summary>
public sealed class RuleEngine
{
    private readonly IEnumerable<IEvaluator> _evaluators;
    private readonly IEnumerable<IReportWriter> _reportWriters;
    private readonly RuleEngineCore _core;
    private readonly ILogger<RuleEngine> _logger;

    public RuleEngine(IEnumerable<IEvaluator> evaluators,
                      IEnumerable<IReportWriter> reportWriters,
                      RuleEngineCore core,
                      ILogger<RuleEngine> logger)
    {
        _evaluators = evaluators;
        _reportWriters = reportWriters;
        _core = core;
        _logger = logger;
    }

    /// <summary>
    /// Executes a full Aegis scan, running all evaluators and producing a consolidated report.
    /// </summary>
    public async Task<AegisReport> RunAsync(string projectPath, ProjectContext context, CancellationToken token = default)
    {
        var report = new AegisReport
        {
            ProjectName = Path.GetFileName(projectPath),
            ProjectPath = projectPath,
            Language = context.Language,
            Framework = context.Framework
        };

        _logger.LogInformation("🚀 Starting Aegis scan for {Project} [{Lang}/{Framework}]",
            report.ProjectName, context.Language, context.Framework);

        var allFacts = new List<EvaluatorResult>();

        // 🧠 1️⃣ Execute Evaluators
        foreach (var evaluator in _evaluators)
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

        // ⚖️ 2️⃣ Evaluate Rules
        var ruleResults = _core.Evaluate(allFacts).ToList();
        report.Results.AddRange(ruleResults);

        _logger.LogInformation("✅ Evaluation completed — {Count} rule violations detected.", ruleResults.Count);

        // 🧾 3️⃣ Export Reports
        foreach (var writer in _reportWriters)
        {
            try
            {
                var output = Path.Combine(projectPath, $"AegisReport.{writer.Format}");
                await writer.WriteAsync(report.Results, output, token);
                _logger.LogInformation("📝 Report exported: {Output}", output);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Report writer {Format} failed.", writer.Format);
            }
        }

        return report;
    }
}
