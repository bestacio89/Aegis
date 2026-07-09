using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Aegis.Infrastructure.Exporters;

/// <summary>
/// 📄 Industrialized Executive PDF Report Exporter using MigraDoc.
/// Generates deterministic, deeply formatted architecture audit reports featuring target file tracing.
/// </summary>
public sealed class PdfReportExporter : IReportExporter
{
    public string Format => "pdf";

    private readonly ILogger<PdfReportExporter> _logger;
    private static bool _fontResolverRegistered;
    private static readonly object _fontResolverLock = new();

    private static readonly Color PrimaryAccentColor = new(41, 128, 185);    // Architectural Blue
    private static readonly Color SecondaryAccentColor = new(52, 73, 94);   // Dark Slate
    private static readonly Color LightRowBgColor = new(244, 246, 249);      // Soft Zebra
    private static readonly Color BorderColor = new(200, 200, 200);          // Neutral Border

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
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                EnsureFontResolverRegistered();

                var document = CreateDocument(report, context, detailLevel);
                var renderer = new PdfDocumentRenderer { Document = document };

                renderer.RenderDocument();
                renderer.PdfDocument.Save(outputPath);
            }, token);

            _logger.LogInformation("📄 Architectural PDF report compiled and saved successfully to -> {Path}", outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Architectural PDF compilation engine execution failed for path -> {Path}", outputPath);
            throw;
        }
    }

    private static void EnsureFontResolverRegistered()
    {
        if (_fontResolverRegistered) return;
        lock (_fontResolverLock)
        {
            if (_fontResolverRegistered) return;
            GlobalFontSettings.FontResolver = new CustomFontResolver();
            _fontResolverRegistered = true;
        }
    }

    private static Document CreateDocument(AegisArchitectureReport report, ProjectArchitectureContext context, ArchitectureReportDetailLevel detailLevel)
    {
        var document = new Document();
        document.Info.Title = "Aegis Architecture Executive Compliance Report";
        document.Info.Author = "Aegis Core Rule Engine";
        document.Info.Subject = $"Compliance Metrics for {report.ProjectName}";

        DefineStyles(document);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;

        section.PageSetup.LeftMargin = "1.5cm";
        section.PageSetup.RightMargin = "1.5cm";
        section.PageSetup.TopMargin = "2cm";
        section.PageSetup.BottomMargin = "2cm";
     
        AddHeader(section);
        AddProjectSummaryCard(section, report, context);
        AddMetricsSection(section, report);
        AddComplianceMatrix(section, report);
        AddTopViolationsLedger(section, report);
        AddLayerAnalysisDetails(section, report);
        AddFooter(section);

        return document;
    }

    private static void DefineStyles(Document document)
    {
        var normal = document.Styles[StyleNames.Normal] ?? throw new InvalidOperationException();
        normal.Font.Name = "DejaVu Sans";
        normal.Font.Size = 9.5;
        normal.Font.Color = new Color(44, 62, 80);
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.OnePtFive;

        var heading1 = document.Styles[StyleNames.Heading1] ?? throw new InvalidOperationException();
        heading1.Font.Name = "DejaVu Sans";
        heading1.Font.Size = 14;
        heading1.Font.Bold = true;
        heading1.Font.Color = PrimaryAccentColor;
        heading1.ParagraphFormat.SpaceBefore = 14;
        heading1.ParagraphFormat.SpaceAfter = 6;

        var heading2 = document.Styles[StyleNames.Heading2] ?? throw new InvalidOperationException();
        heading2.Font.Name = "DejaVu Sans";
        heading2.Font.Size = 11;
        heading2.Font.Bold = true;
        heading2.Font.Color = SecondaryAccentColor;
    }

    private static void ConfigureBaseTable(Table table)
    {
        table.Borders.Color = BorderColor;
        table.Borders.Width = 0.5;
        table.Format.SpaceBefore = 5;
        table.Format.SpaceAfter = 5;
    }

    private static void ApplyRowStyle(Row row, bool isHeader, bool isAlternate)
    {
        row.VerticalAlignment = VerticalAlignment.Center;
        row.TopPadding = 5;
        row.BottomPadding = 5;
        if (isHeader)
        {
            row.Shading.Color = isAlternate ? PrimaryAccentColor : SecondaryAccentColor;
            row.Format.Font.Bold = true;
            row.Format.Font.Color = Colors.White;
        }
        else if (isAlternate)
        {
            row.Shading.Color = LightRowBgColor;
        }
    }

    private static void AddHeader(Section section)
    {
        var header = section.Headers.Primary.AddParagraph();
        header.Format.Alignment = ParagraphAlignment.Right;
        header.AddFormattedText("AEGIS ARCHITECTURAL COMPLIANCE REPORT", TextFormat.Bold).Font.Color = PrimaryAccentColor;
    }

    private static void AddProjectSummaryCard(Section section, AegisArchitectureReport report, ProjectArchitectureContext context)
    {
        section.AddParagraph("Executive Scope Summary").Style = StyleNames.Heading1;
        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("4.5cm");
        table.AddColumn("13.5cm");

        AddRow(table, "Target Project", report.ProjectName, true);
        AddRow(table, "Workspace Path", report.ProjectPath);
        AddRow(table, "Language", context.Language);
        AddRow(table, "Framework", context.Framework);
        AddRow(table, "Style", context.ArchitectureStyle ?? "N/A");
    }

    private static void AddMetricsSection(Section section, AegisArchitectureReport report)
    {
        section.AddParagraph("High-Level Quantitative Metrics").Style = StyleNames.Heading1;
        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("6cm");
        table.AddColumn("12cm");

        AddRow(table, "Files Scanned", report.TotalFilesScanned.ToString());
        AddRow(table, "Total Violations", report.TotalViolations.ToString());
    }

    private static void AddComplianceMatrix(Section section, AegisArchitectureReport report)
    {
        section.AddParagraph("Compliance Index Matrix").Style = StyleNames.Heading1;
        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("8cm");
        table.AddColumn("5cm");
        table.AddColumn("5cm");

        var header = table.AddRow();
        ApplyRowStyle(header, true, false);
        header.Cells[0].AddParagraph("Category");
        header.Cells[1].AddParagraph("Score");
        header.Cells[2].AddParagraph("Status");

        bool alt = false;
        foreach (var score in report.ComplianceScores)
        {
            var row = table.AddRow();
            ApplyRowStyle(row, false, alt);
            row.Cells[0].AddParagraph(score.Key.ToString());
            row.Cells[1].AddParagraph($"{score.Value:0.00}%");
            row.Cells[2].AddParagraph(score.Value >= 80 ? "PASSED" : "WARNING");
            alt = !alt;
        }
    }

    private static void AddTopViolationsLedger(Section section, AegisArchitectureReport report)
    {
        section.AddParagraph("Critical Violations").Style = StyleNames.Heading1;
        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("4cm");
        table.AddColumn("12cm");

        var targets = report.Results.OrderByDescending(x => x.WeightedImpact).Take(12);
        bool alt = false;
        foreach (var item in targets)
        {
            var row = table.AddRow();
            ApplyRowStyle(row, false, alt);
            row.Cells[0].AddParagraph(Path.GetFileName(item.FilePath));
            var p = row.Cells[1].AddParagraph();
            p.AddFormattedText($"{item.RuleName}: ", TextFormat.Bold);
            p.AddText(item.Message);
            alt = !alt;
        }
    }

    private static void AddLayerAnalysisDetails(Section section, AegisArchitectureReport report)
    {
        section.AddParagraph("Layer Cross-Section Analysis").Style = StyleNames.Heading1;
        foreach (var group in report.Results.GroupBy(x => x.Domain))
        {
            section.AddParagraph($"Layer: {group.Key ?? "Core"}").Style = StyleNames.Heading2;
            var table = section.AddTable();
            ConfigureBaseTable(table);
            table.AddColumn("4cm");
            table.AddColumn("12cm");
            bool alt = false;
            foreach (var item in group)
            {
                var row = table.AddRow();
                ApplyRowStyle(row, false, alt);
                row.Cells[0].AddParagraph(Path.GetFileName(item.FilePath));
                row.Cells[1].AddParagraph($"{item.RuleName} -> {item.Message}");
                alt = !alt;
            }
        }
    }

    private static void AddRow(Table table, string label, string value, bool bold = false)
    {
        var row = table.AddRow();
        row.Cells[0].AddParagraph(label).Format.Font.Bold = true;
        row.Cells[1].AddParagraph(value).Format.Font.Bold = bold;
    }

    private static void AddFooter(Section section)
    {
        var f = section.Footers.Primary.AddParagraph();
        f.Format.Alignment = ParagraphAlignment.Center;
        f.AddText($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC • Aegis v1.0");
    }
}