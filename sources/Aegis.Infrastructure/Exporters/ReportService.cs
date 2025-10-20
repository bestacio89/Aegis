using Aegis.Shared.Contracts;
using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Exporters;

/// <summary>
/// 🧾 Centralized service that orchestrates report persistence and multi-format export.
/// Handles PDF, JSON, CSV, and future exporters (Markdown, HTML, etc.).
/// </summary>
public sealed class ReportService : IScopedDependency
{
    private readonly IEnumerable<IReportExporter> _exporters;
    private readonly ILogger<ReportService> _logger;

    public ReportService(IEnumerable<IReportExporter> exporters, ILogger<ReportService> logger)
    {
        _exporters = exporters;
        _logger = logger;
    }

    /// <summary>
    /// Persists and exports all available report formats into the local .aegis/reports directory.
    /// </summary>
    /// <param name="report">The analyzed Aegis report to export.</param>
    /// <param name="context">The project context metadata.</param>
    /// <param name="basePath">The project or working directory base path.</param>
    /// <param name="detailLevel">The desired level of report detail.</param>
    /// <param name="token">Cancellation token.</param>
    public async Task SaveAllAsync(
        AegisReport report,
        ProjectContext context,
        string basePath,
        ReportDetailLevel detailLevel = ReportDetailLevel.FullForensic,
        CancellationToken token = default)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var dir = Path.Combine(basePath, ".aegis", "reports", timestamp);
            Directory.CreateDirectory(dir);

            _logger.LogInformation("🧠 Starting report export for {Project} ({Lang}/{Framework}) [{Detail}]",
                report.ProjectName, context.Language, context.Framework, detailLevel);

            foreach (var exporter in _exporters)
            {
                try
                {
                    var safeFormat = exporter.Format.Replace("/", "_").ToLowerInvariant();
                    var fileName = $"AegisReport_{report.ProjectName}_{safeFormat}.{exporter.Format}";
                    var filePath = Path.Combine(dir, fileName);

                    _logger.LogInformation("📤 Exporting using {Exporter} → {File}", exporter.GetType().Name, filePath);

                    await exporter.ExportAsync(report, context, filePath, detailLevel, token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Exporter {Format} failed for project {Project}", exporter.Format, report.ProjectName);
                }
            }

            _logger.LogInformation("📦 All reports generated successfully ({Mode}) → {Path}", detailLevel, dir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to generate reports for {Project}", report.ProjectName);
            throw;
        }
    }
}
