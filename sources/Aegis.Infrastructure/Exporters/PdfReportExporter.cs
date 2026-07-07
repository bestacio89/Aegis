using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Aegis.Infrastructure.Exporters;

/// <summary>
/// 📄 Executive PDF Report Exporter using MigraDoc.
/// Generates professional architecture audit reports.
/// </summary>
public sealed class PdfReportExporter : IReportExporter
{
    public string Format => "pdf";

    private readonly ILogger<PdfReportExporter> _logger;

    public PdfReportExporter(
        ILogger<PdfReportExporter> logger)
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
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                var document = CreateDocument(
                    report,
                    context,
                    detailLevel);

                var renderer = new PdfDocumentRenderer
                {
                    Document = document
                };

                renderer.RenderDocument();
                renderer.PdfDocument.Save(outputPath);

            }, token);


            _logger.LogInformation(
                "📄 PDF report exported → {Path}",
                outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Failed to generate PDF report → {Path}",
                outputPath);

            throw;
        }
    }


    private static Document CreateDocument(
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        ArchitectureReportDetailLevel detailLevel)
    {
        var document = new Document();

        document.Info.Title =
            "Aegis Architecture Executive Report";

        document.Info.Author =
            "Aegis Architecture Analyzer";


        DefineStyles(document);


        var section = document.AddSection();

        section.PageSetup.PageFormat =
            PageFormat.A4;


        section.PageSetup.TopMargin =
            Unit.FromCentimeter(2);

        section.PageSetup.BottomMargin =
            Unit.FromCentimeter(2);



        AddHeader(section);

        AddProjectInformation(
            section,
            report,
            context);


        AddMetrics(
            section,
            report);


        AddComplianceTable(
            section,
            report);


        AddViolations(
            section,
            report);


        AddLayerAnalysis(
            section,
            report);


        AddFooter(section);


        return document;
    }



    private static void DefineStyles(Document document)
    {
        var normal = document.Styles[StyleNames.Normal]
            ?? throw new InvalidOperationException(
                "MigraDoc Normal style was not found.");

        normal.Font.Name = "DejaVu Sans";
        normal.Font.Size = 10;


        var heading = document.Styles[StyleNames.Heading1]
            ?? throw new InvalidOperationException(
                "MigraDoc Heading1 style was not found.");

        heading.Font.Name = "DejaVu Sans";
        heading.Font.Size = 16;
        heading.Font.Bold = true;
    }



    private static void AddHeader(Section section)
    {
        var paragraph =
            section.Headers.Primary.AddParagraph();

        paragraph.AddText(
            "🧠 Aegis Executive Architecture Report");

        paragraph.Style =
            "Heading1";
    }



    private static void AddProjectInformation(
        Section section,
        AegisArchitectureReport report,
        ProjectArchitectureContext context)
    {
        var p =
            section.AddParagraph();


        p.AddFormattedText(
            $"📁 Project: {report.ProjectName}\n",
            TextFormat.Bold);

        p.AddText(
            $"📂 Path: {report.ProjectPath}\n");

        p.AddText(
            $"🌐 Language: {context.Language}\n");

        p.AddText(
            $"🧩 Framework: {context.Framework}\n");

        p.AddText(
            $"🏗️ Architecture: {context.ArchitectureStyle ?? "Unknown"}\n");

        p.AddText(
            $"🧱 Layer: {context.Layer ?? "Unclassified"}\n");

        p.AddText(
            $"🔨 Build System: {context.BuildSystem}\n");

        p.AddText(
            $"🧠 Confidence: {context.Confidence:P0}");
    }



    private static void AddMetrics(
        Section section,
        AegisArchitectureReport report)
    {
        section.AddParagraph("Project Metrics")
            .Style = "Heading1";


        var table = section.AddTable();

        table.Borders.Width = 0.5;


        // REQUIRED
        table.AddColumn("5cm");
        table.AddColumn("5cm");


        AddMetricRow(
            table,
            "Files scanned",
            report.TotalFilesScanned.ToString());


        AddMetricRow(
            table,
            "Violations",
            report.TotalViolations.ToString());


        AddMetricRow(
            table,
            "Health Index",
            $"{report.Metrics?.ProjectHealthIndex ?? 0:0.00}%");
    }


    private static void AddMetricRow(
        Table table,
        string label,
        string value)
    {
        var row = table.AddRow();

        row.Cells[0]
            .AddParagraph(label);

        row.Cells[1]
            .AddParagraph(value);
    }



    private static void AddComplianceTable(
    Section section,
    AegisArchitectureReport report)
    {
        section.AddParagraph(
            "Compliance by Category")
            .Style = "Heading1";


        var table = section.AddTable();

        table.Borders.Width = 0.5;


        // REQUIRED: define columns before adding rows
        table.AddColumn("5cm");
        table.AddColumn("3cm");
        table.AddColumn("3cm");


        var header = table.AddRow();

        header.Cells[0]
            .AddParagraph("Category");

        header.Cells[1]
            .AddParagraph("Score");

        header.Cells[2]
            .AddParagraph("Status");


        if (!report.ComplianceScores.Any())
        {
            var emptyRow = table.AddRow();

            emptyRow.Cells[0]
                .AddParagraph("No compliance data available");

            emptyRow.Cells[1]
                .AddParagraph("-");

            emptyRow.Cells[2]
                .AddParagraph("-");

            return;
        }


        foreach (var score in report.ComplianceScores)
        {
            var row = table.AddRow();


            row.Cells[0]
                .AddParagraph(
                    score.Key.ToString());


            row.Cells[1]
                .AddParagraph(
                    $"{score.Value:0.00}%");


            row.Cells[2]
                .AddParagraph(
                    score.Value >= 80
                        ? "OK"
                        : score.Value >= 50
                            ? "Warning"
                            : "Critical");
        }
    }



    private static void AddViolations(
        Section section,
        AegisArchitectureReport report)
    {
        section.AddParagraph(
            "🔍 Top Violations")
            .Style = "Heading1";


        foreach (var violation in report.Results
            .OrderByDescending(x => x.WeightedImpact)
            .Take(10))
        {
            section.AddParagraph(
                $"• {violation.RuleName} [{violation.Severity}] → {violation.Message}");
        }
    }



    private static void AddLayerAnalysis(
        Section section,
        AegisArchitectureReport report)
    {
        section.AddParagraph(
            "🏗️ Layer Analysis")
            .Style = "Heading1";


        foreach (var layer in report.Results
            .GroupBy(x => x.Domain))
        {
            section.AddParagraph(
                $"📦 {layer.Key}")
                .Style = "Heading2";


            foreach (var item in layer.Take(5))
            {
                section.AddParagraph(
                    $"{item.RuleName} | {item.Severity} | {item.Message}");
            }
        }
    }



    private static void AddFooter(Section section)
    {
        section.Footers.Primary.AddParagraph(
            $"Generated {DateTime.UtcNow:u} • Aegis v1.0")
            .Format.Alignment =
            ParagraphAlignment.Center;
    }
}