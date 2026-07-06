using System.Text;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Exporters;

/// <summary>
/// 📝 Executive Markdown Report Exporter
/// Generates a GitHub-friendly architecture report.
/// </summary>
public sealed class MarkdownReportExporter : IReportExporter
{
    public string Format => "markdown";

    private readonly ILogger<MarkdownReportExporter> _logger;

    public MarkdownReportExporter(
        ILogger<MarkdownReportExporter> logger)
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
            var md = new StringBuilder();

            md.AppendLine("# 🧠 Aegis Executive Architecture Report");
            md.AppendLine();

            md.AppendLine($"**Generated:** {DateTime.UtcNow:u}");
            md.AppendLine();

            md.AppendLine("---");
            md.AppendLine();

            // ============================================================
            // Project Information
            // ============================================================

            md.AppendLine("## 📁 Project Information");
            md.AppendLine();

            md.AppendLine($"| Property | Value |");
            md.AppendLine($"|----------|-------|");
            md.AppendLine($"| Project | {report.ProjectName} |");
            md.AppendLine($"| Path | `{report.ProjectPath}` |");
            md.AppendLine($"| Language | {context.Language} |");
            md.AppendLine($"| Framework | {context.Framework} |");
            md.AppendLine($"| Architecture | {context.ArchitectureStyle ?? "Unknown"} |");
            md.AppendLine($"| Layer | {context.Layer ?? "Unclassified"} |");
            md.AppendLine($"| Build System | {context.BuildSystem} |");
            md.AppendLine($"| Confidence | {context.Confidence:P0} |");

            md.AppendLine();

            // ============================================================
            // Health Summary
            // ============================================================

            md.AppendLine("## 📊 Project Health Summary");
            md.AppendLine();

            md.AppendLine("| Metric | Value |");
            md.AppendLine("|-------|------:|");
            md.AppendLine($"| Files Scanned | {report.TotalFilesScanned} |");
            md.AppendLine($"| Total Violations | {report.TotalViolations} |");
            md.AppendLine($"| Health Index | **{report.Metrics.ProjectHealthIndex:0.00}%** |");

            md.AppendLine();

            // ============================================================
            // Compliance
            // ============================================================

            md.AppendLine("## 🧩 Compliance by Category");
            md.AppendLine();

            md.AppendLine("| Category | Score | Status |");
            md.AppendLine("|----------|------:|--------|");

            foreach (var kv in report.ComplianceScores.OrderBy(x => x.Key.ToString()))
            {
                string status =
                    kv.Value >= 80 ? "✅ OK" :
                    kv.Value >= 50 ? "⚠️ Warning" :
                    "❌ Critical";

                md.AppendLine(
                    $"| {kv.Key} | {kv.Value:0.00}% | {status} |");
            }

            md.AppendLine();

            // ============================================================
            // Top Violations
            // ============================================================

            md.AppendLine("## 🔍 Top 10 Violations");
            md.AppendLine();

            foreach (var violation in report.Results
                         .OrderByDescending(x => x.WeightedImpact)
                         .Take(10))
            {
                md.AppendLine($"### {violation.RuleName}");
                md.AppendLine();

                md.AppendLine($"- **Severity:** {violation.Severity}");
                md.AppendLine($"- **Category:** {violation.Category}");
                md.AppendLine($"- **Target:** `{violation.Target}`");
                md.AppendLine($"- **Impact Score:** {violation.ImpactScore:0.00}");
                md.AppendLine($"- **Weighted Impact:** {violation.WeightedImpact:0.00}");
                md.AppendLine($"- **Message:** {violation.Message}");
                md.AppendLine();
            }

            // ============================================================
            // Layer Analysis
            // ============================================================

            md.AppendLine("## 🏗️ Layer Analysis");
            md.AppendLine();

            foreach (var layer in report.Results
                         .GroupBy(r => r.Domain)
                         .OrderBy(g => g.Key))
            {
                md.AppendLine($"### {layer.Key}");
                md.AppendLine();

                md.AppendLine("| Rule | Severity | Message |");
                md.AppendLine("|------|----------|---------|");

                foreach (var result in layer.Take(10))
                {
                    md.AppendLine(
                        $"| {Escape(result.RuleName)} | {result.Severity} | {Escape(result.Message)} |");
                }

                md.AppendLine();
            }

            // ============================================================
            // Statistics
            // ============================================================

            md.AppendLine("## 📈 Statistics");
            md.AppendLine();

            var severityGroups = report.Results
                .GroupBy(r => r.Severity)
                .OrderByDescending(g => g.Count());

            md.AppendLine("| Severity | Count |");
            md.AppendLine("|----------|------:|");

            foreach (var group in severityGroups)
            {
                md.AppendLine($"| {group.Key} | {group.Count()} |");
            }

            md.AppendLine();

            // ============================================================
            // Footer
            // ============================================================

            md.AppendLine("---");
            md.AppendLine();
            md.AppendLine("*Generated by Aegis Architecture Analyzer v1.0*");
            md.AppendLine();

            await File.WriteAllTextAsync(
                outputPath,
                md.ToString(),
                Encoding.UTF8,
                token);

            _logger.LogInformation(
                "📝 Markdown report exported → {Path}",
                outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Failed to generate Markdown report → {Path}",
                outputPath);

            throw;
        }
    }

    private static string Escape(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text
            .Replace("|", "\\|")
            .Replace("\r", "")
            .Replace("\n", "<br/>");
    }
}