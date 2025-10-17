using System.Text.Json;
using Aegis.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Core.Reports;

/// <summary>
/// Handles post-processing, archival, and metadata enrichment of reports.
/// </summary>
public class ReportService
{
    private readonly ILogger<ReportService> _logger;

    public ReportService(ILogger<ReportService> logger)
    {
        _logger = logger;
    }

    public Task SaveReportAsync(AegisReport report, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(report, options);
        File.WriteAllText(outputPath, json);

        _logger.LogInformation("💾 Aegis report saved at {Path}", outputPath);
        return Task.CompletedTask;
    }
}
