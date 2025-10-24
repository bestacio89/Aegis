using System.Globalization;
using System.Text;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Aegis.Shared.Models.Rules;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Exporters;

/// <summary>
/// Exports detailed Aegis rule results to CSV for analytics, dashboards, and forensics.
/// Includes full project context and weighting data.
/// </summary>
public sealed class CsvReportExporter : IReportExporter
{
    public string Format => "csv";
    private readonly ILogger<CsvReportExporter> _logger;

    public CsvReportExporter(ILogger<CsvReportExporter> logger)
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
        var sb = new StringBuilder();

        // 🧭 HEADER
        sb.AppendLine(string.Join(",",
        [
            "ProjectName",
            "Language",
            "Framework",
            "ArchitectureStyle",
            "Domain",
            "Layer",
            "RuleId",
            "RuleName",
            "Category",
            "Severity",
            "FilePath",
            "IsCompliant",
            "WeightedImpact",
            "ImpactScore",
            "Message",
            "Recommendation",
            "Timestamp",
            "Evaluator",
            "AnalyzerVersion"
        ]));

        // 🧮 DATA
        foreach (var r in report.Results)
        {
            var safeMessage = SanitizeCsv(r.Message);
            var safeRecommendation = SanitizeCsv(r.Recommendation);
            var line = string.Join(",",
            [
                SanitizeCsv(report.ProjectName),
                SanitizeCsv(report.Language),
                SanitizeCsv(report.Framework ?? "Unknown"),
                SanitizeCsv(context.ArchitectureStyle ?? "Unknown"),
                SanitizeCsv(r.Domain ?? "General"),
                SanitizeCsv(context.Layer ?? "N/A"),
                SanitizeCsv(r.RuleId),
                SanitizeCsv(r.RuleName),
                r.Category.ToString(),
                r.Severity.ToString(),
                SanitizeCsv(r.FilePath),
                r.IsCompliant.ToString(),
                r.WeightedImpact.ToString("0.00", CultureInfo.InvariantCulture),
                r.ImpactScore.ToString("0.00", CultureInfo.InvariantCulture),
                safeMessage,
                safeRecommendation,
                r.Timestamp.ToString("u"),
                SanitizeCsv(r.DetectedBy),
                SanitizeCsv(r.AnalyzerVersion)
            ]);

            sb.AppendLine(line);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, sb.ToString(), token);

        _logger.LogInformation("📊 CSV report exported ({Count} rules) → {Path}", report.Results.Count, outputPath);
    }

    private static string SanitizeCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var escaped = value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", " ");
        return $"\"{escaped}\"";
    }
}
