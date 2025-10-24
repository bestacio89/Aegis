using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Aegis.Infrastructure.Exporters;

/// <summary>
/// 📄 Executive PDF Report Exporter (Professional summary view)
/// </summary>
public sealed class PdfReportExporter : IReportExporter
{
    public string Format => "pdf";
    private readonly ILogger<PdfReportExporter> _logger;

    public PdfReportExporter(ILogger<PdfReportExporter> logger)
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
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(TextStyle.Default.FontSize(11).FontFamily("Arial"));

                    // HEADER
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text($"🧠 Aegis Executive Report")
                            .FontSize(22).Bold().FontColor(Colors.Blue.Medium);
                        row.ConstantItem(100).AlignRight().Text(DateTime.UtcNow.ToString("u"))
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                    });

                    // CONTENT
                    page.Content().Column(col =>
                    {
                        col.Item().PaddingBottom(10).Text($"📁 Project: {report.ProjectName}")
                            .Bold().FontSize(14);
                        col.Item().Text($"📂 Path: {report.ProjectPath}").FontSize(10);
                        col.Item().Text($"🌐 Language: {context.Language}").FontSize(10);
                        col.Item().Text($"🧩 Framework: {context.Framework}").FontSize(10);
                        col.Item().Text($"🏗️ Architecture: {context.ArchitectureStyle ?? "Unknown"}").FontSize(10);
                        col.Item().Text($"🧱 Layer: {context.Layer ?? "Unclassified"}").FontSize(10);
                        col.Item().Text($"🔨 Build System: {context.BuildSystem}").FontSize(10);
                        col.Item().Text($"🧠 Confidence: {context.Confidence:P0}").FontSize(10);
                        col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                        // SUMMARY METRICS
                        col.Item().Text("📊 Project Health Summary / Résumé de la santé du projet")
                            .Bold().FontSize(13).FontColor(Colors.Blue.Medium);

                        col.Item().PaddingVertical(4).Row(row =>
                        {
                            row.RelativeItem().Text($"Total Files Scanned:\n{report.TotalFilesScanned}");
                            row.RelativeItem().Text($"Total Violations:\n{report.TotalViolations}")
                                .FontColor(Colors.Red.Medium);
                            row.RelativeItem().Text($"Health Index:\n{report.Metrics.ProjectHealthIndex:0.00}%")
                                .Bold()
                                .FontColor(report.Metrics.ProjectHealthIndex >= 80
                                    ? Colors.Green.Medium
                                    : report.Metrics.ProjectHealthIndex >= 50
                                        ? Colors.Orange.Medium
                                        : Colors.Red.Medium);
                        });

                        col.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                        // COMPLIANCE TABLE
                        col.Item().Text("🧩 Compliance by Category / Conformité par catégorie")
                            .Bold().FontSize(13);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                            });

                            table.Header(h =>
                            {
                                h.Cell().Text("Category / Catégorie").Bold();
                                h.Cell().AlignCenter().Text("Score");
                                h.Cell().AlignCenter().Text("Status");
                            });

                            foreach (var kv in report.ComplianceScores)
                            {
                                var score = kv.Value;
                                var color = score >= 80 ? Colors.Green.Medium :
                                            score >= 50 ? Colors.Orange.Medium :
                                            Colors.Red.Medium;

                                table.Cell().Text(kv.Key.ToString());
                                table.Cell().AlignCenter().Text($"{score:0.00}%");
                                table.Cell().AlignCenter()
                                    .Text(score >= 80 ? "✅ OK" :
                                        score >= 50 ? "⚠️ Warning" : "❌ Critical")
                                    .FontColor(color);
                            }
                        });

                        col.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                        // TOP VIOLATIONS
                        col.Item().Text("🔍 Top 10 Violations / Principales violations")
                            .Bold().FontSize(13).FontColor(Colors.Red.Medium);

                        foreach (var v in report.Results
                                                 .OrderByDescending(r => r.WeightedImpact)
                                                 .Take(10))
                        {
                            col.Item().Text($"• {v.RuleName} [{v.Severity}] → {v.Message}")
                                .FontSize(10)
                                .FontColor(v.Severity switch
                                {
                                    ArchitectureRuleSeverity.Critical => Colors.Red.Medium,
                                    ArchitectureRuleSeverity.High => Colors.Orange.Medium,
                                    _ => Colors.Grey.Darken2
                                });
                        }

                        col.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                        // LAYER ANALYSIS
                        col.Item().Text("🏗️ Layer-by-Layer Analysis / Analyse par couche")
                            .Bold().FontSize(13).FontColor(Colors.Blue.Medium);

                        var byLayer = report.Results.GroupBy(r => r.Domain).OrderBy(g => g.Key);

                        foreach (var group in byLayer)
                        {
                            col.Item().PaddingTop(6).Text($"📦 {group.Key} Layer")
                                .Bold().FontSize(12).FontColor(Colors.Grey.Darken1);

                            col.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(1);
                                    c.RelativeColumn(4);
                                });

                                t.Header(h =>
                                {
                                    h.Cell().Text("Rule").Bold();
                                    h.Cell().Text("Severity").Bold();
                                    h.Cell().Text("Message").Bold();
                                });

                                foreach (var r in group.Take(5))
                                {
                                    t.Cell().Text(r.RuleName);
                                    t.Cell().Text(r.Severity.ToString());
                                    t.Cell().Text(r.Message);
                                }
                            });
                        }
                    });

                    // FOOTER
                    page.Footer()
                        .AlignCenter()
                        .Text($"Generated {DateTime.UtcNow:u} • Aegis v1.0 — Executive Summary Report")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken1);
                });
            });

            await Task.Run(() => doc.GeneratePdf(outputPath), token);
            _logger.LogInformation("📄 PDF report exported → {Path}", outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to generate PDF report → {Path}", outputPath);
            throw;
        }
    }
}
