using Aegis.Infrastructure.Aggregation;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Aegis.Infrastructure.Exporters;

public sealed class ForensicPdfExporter : IReportExporter
{
    public string Format => "pdf.forensic";
    private readonly ILogger<ForensicPdfExporter> _logger;
    private readonly LayerAggregator _aggregator;

    public ForensicPdfExporter(ILogger<ForensicPdfExporter> logger, LayerAggregator aggregator)
    {
        _logger = logger;
        _aggregator = aggregator;
    }

    public async Task ExportAsync(
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        string outputPath,
        ArchitectureReportDetailLevel detailLevel = ArchitectureReportDetailLevel.FullForensic,
        CancellationToken token = default)
    {
        var categories = _aggregator.BuildCategorySummaries(report.Results);

        var doc = Document.Create(container =>
        {
            // ─── COVER PAGE ─────────────────────────────────────────────
            container.Page(page =>
            {
                page.Margin(40);
                page.Header().AlignCenter()
                    .Text("🧠 Aegis Forensic Architecture Audit Report\nRapport d’Audit Architectural Forensique")
                    .FontSize(22).Bold().FontColor(Colors.Blue.Medium);

                page.Content().Column(col =>
                {
                    col.Item().Text($"Project / Projet : {report.ProjectName}").Bold();
                    col.Item().Text($"Language / Langage : {context.Language} / {context.Framework}");
                    col.Item().Text($"Architecture / Architecture : {context.ArchitectureStyle}");
                    col.Item().Text($"Health Index / Indice de Santé : {report.Metrics.ProjectHealthIndex:0.00}%");
                    col.Item().Text($"Violations / Violations : {report.TotalViolations}");
                    col.Item().PaddingTop(10)
                        .Text("Generated automatically by Aegis — deterministic architecture analysis tool.\nGénéré automatiquement par Aegis — outil d’analyse architecturale déterministe.")
                        .FontSize(10).FontColor(Colors.Grey.Darken2);
                });

                page.Footer().AlignCenter()
                    .Text($"Generated / Généré : {DateTime.UtcNow:u} — Aegis v1.0 | Franz Technologies © 2025")
                    .FontSize(9);
            });

            // ─── SUMMARY-ONLY MODE ─────────────────────────────────────
            if (detailLevel == ArchitectureReportDetailLevel.SummaryOnly)
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Header().Text("📊 Executive Summary / Résumé Exécutif")
                        .FontSize(18).Bold().FontColor(Colors.Blue.Darken2);

                    page.Content().Column(col =>
                    {
                        foreach (var category in categories.Values)
                        {
                            col.Item().Text($"• {category.CategoryName} — {category.HealthIndex:0.0}% health, {category.TotalViolations} violations")
                                .FontSize(11)
                                .FontColor(category.HealthIndex switch
                                {
                                    > 80 => Colors.Green.Darken2,
                                    > 60 => Colors.Orange.Darken2,
                                    _ => Colors.Red.Medium
                                });
                        }
                    });

                    page.Footer().AlignRight().Text("Summary Mode — Aegis").FontSize(9);
                });
                return;
            }

            // ─── LAYERED OR FULL MODE ─────────────────────────────────
            foreach (var category in categories.Values)
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Header().Text($"📚 Category / Catégorie : {category.CategoryName}")
                        .FontSize(18).Bold().FontColor(Colors.Blue.Darken2);

                    page.Content().Column(col =>
                    {
                        col.Item().Text($"Health Index / Indice de Santé : {category.HealthIndex:0.0}%");
                        col.Item().Text($"Violations / Violations : {category.TotalViolations}");

                        foreach (var (layerName, layer) in category.Layers)
                        {
                            col.Item().PaddingVertical(10)
                                .Text($"🧩 Layer / Couche : {layerName}")
                                .Bold().FontColor(Colors.Grey.Darken3);

                            col.Item().Text($"Violations : {layer.Violations} — Health : {layer.HealthIndex:0.0}%");

                            if (detailLevel >= ArchitectureReportDetailLevel.Layered)
                            {
                                col.Item().PaddingVertical(5)
                                    .Text("Top Violations / Principales Violations").Bold();
                                foreach (var v in layer.TopViolations.Take(5))
                                    col.Item().Text($"• {v.RuleName} [{v.Severity}] — {v.Message}")
                                        .FontSize(10).FontColor(Colors.Grey.Darken2);
                            }

                            if (detailLevel == ArchitectureReportDetailLevel.FullForensic && layer.Recommendations.Any())
                            {
                                col.Item().Text("Recommendations / Recommandations").Bold();
                                foreach (var rec in layer.Recommendations)
                                    col.Item().Text($"- {rec}");
                            }
                        }
                    });

                    page.Footer().AlignRight()
                        .Text($"Category {category.CategoryName} — {DateTime.UtcNow:u}")
                        .FontSize(9);
                });
            }
        });

        await Task.Run(() => doc.GeneratePdf(outputPath), token);
        _logger.LogInformation("📑 Forensic PDF ({Mode}) generated → {Path}", detailLevel, outputPath);
    }
}
