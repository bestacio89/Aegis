using Aegis.Core.Analysis;
using Aegis.Core.RuleEngines;
using Aegis.Shared.Models;
using Franz.Common.Logging;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aegis.Sdk;

/// <summary>
/// Executes full deterministic Aegis analyses on projects.
/// </summary>
public sealed class AegisRunner
{
    private readonly RuleEngine _engine;
    private readonly ILogger<AegisRunner> _logger;

    public AegisRunner(RuleEngine engine, ILogger<AegisRunner> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// Executes an Aegis analysis end-to-end with the configured RuleEngine.
    /// </summary>
    /// <param name="projectPath">Root path of the project to analyze.</param>
    /// <param name="policyPath">Optional path to the policy file (default: aegis.policy.json).</param>
    /// <param name="exportJson">Whether to export a JSON report (default: true).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>0 = success, 1 = violations, -1 = failure.</returns>
    public async Task<int> RunAsync(
        string projectPath,
        string policyPath = "aegis.policy.json",
        bool exportJson = true,
        CancellationToken token = default)
    {
        try
        {
            var context = ProjectContextDetector.Detect(projectPath);

            _logger.LogInformation("🚀 Starting Aegis analysis for {Lang}/{Framework} at {Path}",
                context.Language, context.Framework, projectPath);

            var report = await _engine.RunAsync(projectPath, context, token);

            _logger.LogInformation("✅ Scan completed — {Violations} violations across {Files} files.",
                report.TotalViolations, report.TotalFilesScanned);

            if (exportJson)
            {
                var outputPath = Path.Combine(projectPath, "AegisReport.json");
                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(outputPath, json, token);
                _logger.LogInformation("📝 Report exported successfully → {Output}", outputPath);
            }

            _logger.LogInformation("📊 Summary → {Project}: {Issues} issues, {Files} files scanned.",
                report.ProjectName, report.TotalViolations, report.TotalFilesScanned);

            return report.TotalViolations == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Aegis analysis failed for {Path}", projectPath);
            return -1;
        }
    }
}
