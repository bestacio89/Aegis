using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Exporters;

/// <summary>
/// 🧾 Exports the full Aegis analysis report in JSON format.
/// Includes project context, metrics, and rule evaluations.
/// Adapts verbosity according to <see cref="ArchitectureReportDetailLevel"/>.
/// </summary>
public sealed class JsonReportExporter : IReportExporter
{
    public string Format => "json";
    private readonly ILogger<JsonReportExporter> _logger;

    public JsonReportExporter(ILogger<JsonReportExporter> logger)
    {
        _logger = logger;
    }

    public async Task ExportAsync(
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        string outputPath,
        ArchitectureReportDetailLevel detailLevel = ArchitectureReportDetailLevel.FullForensic,
        CancellationToken token = default)
    {
        try
        {
            // ─────────────────────────────
            // 🧩 Build export object
            // ─────────────────────────────
            var export = new
            {
                Report = new
                {
                    report.ProjectName,
                    report.ProjectPath,
                    report.Language,
                    report.Framework,
                    report.ScanDate,
                    report.TotalFilesScanned,
                    report.TotalViolations,
                    Health = report.Metrics.ProjectHealthIndex,
                    report.ComplianceScores,
                    report.Summary
                },

                Context = new
                {
                    context.Language,
                    context.Framework,
                    context.ArchitectureStyle,
                    context.Layer,
                    context.Nature,
                    context.DomainType,
                    context.BuildSystem,
                    context.TargetRuntime,
                    context.IsDeployable,
                    context.IsDockerized,
                    context.UsesKubernetes,
                    context.Confidence,
                    context.FileCount,
                    context.LinesOfCode,
                    context.AverageComplexity,
                    Metadata = context.Metadata
                },

                Overview = new
                {
                    Mode = detailLevel.ToString(),
                    Timestamp = DateTime.UtcNow,
                    AegisVersion = "1.0.0",
                    Engine = "Deterministic Architecture Audit Engine",
                    GeneratedBy = Environment.UserName
                },

                Results = detailLevel switch
                {
                    ArchitectureReportDetailLevel.SummaryOnly => (object)report.Results
                        .GroupBy(r => r.Category)
                        .Select(g => new
                        {
                            Category = g.Key.ToString(),
                            Violations = g.Count(),
                            AvgImpact = g.Average(r => r.WeightedImpact),
                            AvgScore = g.Average(r => r.ImpactScore),
                            TopRule = g.OrderByDescending(r => r.WeightedImpact)
                                       .FirstOrDefault()?.RuleName
                        }),

                    ArchitectureReportDetailLevel.Layered => (object)report.Results
                        .GroupBy(r => new { r.Category, r.Domain })
                        .Select(g => new
                        {
                            g.Key.Category,
                            g.Key.Domain,
                            Violations = g.Count(),
                            SeverityBreakdown = g.GroupBy(r => r.Severity)
                                                 .ToDictionary(x => x.Key.ToString(), x => x.Count())
                        }),

                    _ => (object)report.Results.Select(r => new
                    {
                        r.RuleId,
                        r.RuleName,
                        Category = r.Category.ToString(),
                        r.Severity,
                        r.FilePath,
                        r.IsCompliant,
                        r.Domain,
                        r.WeightedImpact,
                        r.ImpactScore,
                        r.Message,
                        r.Recommendation,
                        r.Timestamp,
                        r.DetectedBy,
                        r.AnalyzerVersion
                    })
                }
            };

            // ─────────────────────────────
            // ⚙️ JSON options
            // ─────────────────────────────
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            // ─────────────────────────────
            // 💾 Write to file
            // ─────────────────────────────
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await using var stream = File.Create(outputPath);
            await JsonSerializer.SerializeAsync(stream, export, options, token);

            _logger.LogInformation("📝 JSON report ({Mode}) exported → {Path}", detailLevel, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to export JSON report → {Path}", outputPath);
            throw;
        }
    }
}
