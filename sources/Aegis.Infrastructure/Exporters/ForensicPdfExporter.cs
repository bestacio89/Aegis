using Aegis.Infrastructure.Aggregation;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;

namespace Aegis.Infrastructure.Exporters;

/// <summary>
/// 📑 Forensic PDF Report Exporter.
/// Generates detailed architectural audit reports using MigraDoc/PDFsharp.
/// </summary>
public sealed class ForensicPdfExporter : IReportExporter
{
    public string Format => "pdf.forensic";

    private readonly ILogger<ForensicPdfExporter> _logger;
    private readonly LayerAggregator _aggregator;


    public ForensicPdfExporter(
        ILogger<ForensicPdfExporter> logger,
        LayerAggregator aggregator)
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
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                var categories =
                    _aggregator.BuildCategorySummaries(report.Results);


                var document =
                    CreateDocument(
                        report,
                        context,
                        categories,
                        detailLevel);


                var renderer =
                    new PdfDocumentRenderer
                    {
                        Document = document
                    };


                renderer.RenderDocument();

                renderer.PdfDocument.Save(outputPath);

            }, token);


            _logger.LogInformation(
                "📑 Forensic PDF ({Mode}) generated → {Path}",
                detailLevel,
                outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Failed generating forensic PDF → {Path}",
                outputPath);

            throw;
        }
    }



    private static Document CreateDocument(
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        IReadOnlyDictionary<string, CategorySummary> categories,
        ArchitectureReportDetailLevel detailLevel)
    {
        var document = new Document();

        ConfigureStyles(document);


        AddCoverPage(
            document,
            report,
            context);


        if (detailLevel == ArchitectureReportDetailLevel.SummaryOnly)
        {
            AddSummaryReport(
                document,
                categories);

            return document;
        }


        AddForensicSections(
            document,
            categories,
            detailLevel);


        return document;
    }



    private static void ConfigureStyles(Document document)
    {
        document.Info.Title =
            "Aegis Forensic Architecture Audit";

        document.Info.Author =
            "Aegis";


        var normal = GetStyle(
            document,
            StyleNames.Normal);

        normal.Font.Name = "DejaVu Sans";
        normal.Font.Size = 10;


        var heading = GetStyle(
            document,
            StyleNames.Heading1);

        heading.Font.Name = "DejaVu Sans";
        heading.Font.Size = 18;
        heading.Font.Bold = true;


        var subHeading = GetStyle(
            document,
            StyleNames.Heading2);

        subHeading.Font.Name = "DejaVu Sans";
        subHeading.Font.Size = 13;
        subHeading.Font.Bold = true;
    }


    private static Style GetStyle(
        Document document,
        string styleName)
    {
        return document.Styles[styleName]
            ?? throw new InvalidOperationException(
                $"Required MigraDoc style '{styleName}' was not found.");
    }



    private static void AddCoverPage(
        Document document,
        AegisArchitectureReport report,
        ProjectArchitectureContext context)
    {
        var section =
            document.AddSection();


        section.PageSetup.PageFormat =
            PageFormat.A4;


        var title =
            section.AddParagraph();

        title.Style =
            "Heading1";

        title.AddText(
            "🧠 Aegis Forensic Architecture Audit Report");


        section.AddParagraph(
            "Rapport d’Audit Architectural Forensique");


        var info =
            section.AddParagraph();


        info.AddFormattedText(
            $"Project / Projet: {report.ProjectName}\n",
            TextFormat.Bold);


        info.AddText(
            $"Language / Langage: {context.Language}\n");


        info.AddText(
            $"Framework: {context.Framework}\n");


        info.AddText(
            $"Architecture: {context.ArchitectureStyle ?? "Unknown"}\n");


        info.AddText(
            $"Health Index: {report.Metrics.ProjectHealthIndex:0.00}%\n");


        info.AddText(
            $"Violations: {report.TotalViolations}");


        section.AddParagraph(
            "Generated automatically by Aegis — deterministic architecture analysis tool.");


        AddFooter(section);
    }



    private static void AddSummaryReport(
        Document document,
        IReadOnlyDictionary<string, CategorySummary> categories)
    {
        var section =
            document.AddSection();


        section.AddParagraph(
            "📊 Executive Summary / Résumé Exécutif")
            .Style =
            "Heading1";


        foreach (var category in categories.Values)
        {
            var paragraph =
                section.AddParagraph();


            paragraph.AddText(
                $"• {category.CategoryName}");


            paragraph.AddText(
                $" — Health {category.HealthIndex:0.0}%");


            paragraph.AddText(
                $" — Violations {category.TotalViolations}");
        }


        AddFooter(section);
    }



    private static void AddForensicSections(
        Document document,
        IReadOnlyDictionary<string, CategorySummary> categories,
        ArchitectureReportDetailLevel detailLevel)
    {
        foreach (var category in categories.Values)
        {
            var section =
                document.AddSection();


            section.AddParagraph(
                $"📚 Category: {category.CategoryName}")
                .Style =
                "Heading1";


            section.AddParagraph(
                $"Health Index: {category.HealthIndex:0.0}%");


            section.AddParagraph(
                $"Violations: {category.TotalViolations}");



            foreach (var layer in category.Layers)
            {
                AddLayerSection(
                    section,
                    layer.Key,
                    layer.Value,
                    detailLevel);
            }


            AddFooter(section);
        }
    }



    private static void AddLayerSection(
        Section section,
        string layerName,
        dynamic layer,
        ArchitectureReportDetailLevel detailLevel)
    {
        section.AddParagraph(
            $"🧩 Layer: {layerName}")
            .Style =
            "Heading2";


        section.AddParagraph(
            $"Violations: {layer.Violations}");


        section.AddParagraph(
            $"Health: {layer.HealthIndex:0.0}%");



        if (detailLevel >= ArchitectureReportDetailLevel.Layered)
        {
            section.AddParagraph(
                "Top Violations")
                .Style =
                "Heading2";


            foreach (var violation in layer.TopViolations.Take(5))
            {
                section.AddParagraph(
                    $"• {violation.RuleName} [{violation.Severity}] - {violation.Message}");
            }
        }



        if (detailLevel == ArchitectureReportDetailLevel.FullForensic &&
           layer.Recommendations.Any())
        {
            section.AddParagraph(
                "Recommendations")
                .Style =
                "Heading2";


            foreach (var recommendation in layer.Recommendations)
            {
                section.AddParagraph(
                    $"- {recommendation}");
            }
        }
    }



    private static void AddFooter(
        Section section)
    {
        section.Footers.Primary
            .AddParagraph(
                $"Generated {DateTime.UtcNow:u} — Aegis v1.0")
            .Format.Alignment =
            ParagraphAlignment.Center;
    }
}