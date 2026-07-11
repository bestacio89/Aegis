using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using System.Globalization;

namespace Aegis.Infrastructure.Exporters;

/// <summary>
/// 📄 Industrialized Executive PDF Report Exporter using MigraDoc.
/// Generates deterministic, deeply formatted architecture audit reports featuring target file tracing.
/// Always renders full detail across all layers and violations — a PDF is the situational record of
/// everything found, regardless of the requested detailLevel. detailLevel is accepted for interface
/// compatibility but intentionally not used to truncate content here.
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
        ReportLanguage language = ReportLanguage.English,
        CancellationToken token = default)
    {
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                EnsureFontResolverRegistered();

                var document = CreateDocument(report, context, language);
                var renderer = new PdfDocumentRenderer { Document = document };

                renderer.RenderDocument();
                renderer.PdfDocument.Save(outputPath);
            }, token);

            _logger.LogInformation(
                "📄 Architectural PDF report ({Language}) compiled and saved successfully to -> {Path}",
                language, outputPath);
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

    /// <summary>
    /// Minimal inline localization helper. English/French only, by design —
    /// this exporter is the only consumer, so a full resource-file setup would be overkill for now.
    /// </summary>
    private static string L(ReportLanguage lang, string en, string fr) =>
        lang == ReportLanguage.French ? fr : en;

    private static string LocalizeCategory(string category, ReportLanguage lang)
    {
        if (lang != ReportLanguage.French) return category;

        return category switch
        {
            "Maintainability" => "Maintenabilité",
            "Performance" => "Performance",
            "Security" => "Sécurité",
            "Architecture" => "Architecture",
            "Design" => "Conception",
            "Style" => "Style",
            "Documentation" => "Documentation",
            "Resilience" => "Résilience",
            "Quality" => "Qualité",
            "BestPractices" => "Bonnes Pratiques",
            "Infrastructure" => "Infrastructure",
            "Testing" => "Tests",
            "Persistence" => "Persistance",
            "General" => "Général",
            "Dependency" => "Dépendance",
            "Naming" => "Nommage",
            "Coupling" => "Couplage",
            "Other" => "Autre",
            _ => category
        };
    }

    private static Document CreateDocument(AegisArchitectureReport report, ProjectArchitectureContext context, ReportLanguage language)
    {
        var document = new Document();
        document.Info.Title = L(language, "Aegis Architecture Executive Compliance Report", "Rapport Exécutif de Conformité Architecturale Aegis");
        document.Info.Author = "Aegis Core Rule Engine";
        document.Info.Subject = $"{L(language, "Compliance Metrics for", "Métriques de Conformité pour")} {report.ProjectName}";

        DefineStyles(document);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;

        section.PageSetup.LeftMargin = "1.5cm";
        section.PageSetup.RightMargin = "1.5cm";
        section.PageSetup.TopMargin = "2cm";
        section.PageSetup.BottomMargin = "2cm";

        AddHeader(section, language);
        AddProjectSummaryCard(section, report, context, language);
        AddMetricsSection(section, report, language);
        AddComplianceMatrix(section, report, language);
        AddViolationsLedger(section, report, language);
        AddLayerAnalysisDetails(section, report, language);
        AddFooter(section, language);

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

    private static void AddHeader(Section section, ReportLanguage language)
    {
        var header = section.Headers.Primary.AddParagraph();
        header.Format.Alignment = ParagraphAlignment.Right;
        header.AddFormattedText(
            L(language, "AEGIS ARCHITECTURAL COMPLIANCE REPORT", "RAPPORT DE CONFORMITÉ ARCHITECTURALE AEGIS"),
            TextFormat.Bold).Font.Color = PrimaryAccentColor;
    }

    private static void AddProjectSummaryCard(Section section, AegisArchitectureReport report, ProjectArchitectureContext context, ReportLanguage language)
    {
        section.AddParagraph(L(language, "Executive Scope Summary", "Synthèse Exécutive du Périmètre")).Style = StyleNames.Heading1;
        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("4.5cm");
        table.AddColumn("13.5cm");

        AddRow(table, L(language, "Target Project", "Projet Cible"), report.ProjectName, true);
        AddRow(table, L(language, "Workspace Path", "Chemin du Projet"), report.ProjectPath);
        AddRow(table, L(language, "Language", "Langage"), context.Language);
        AddRow(table, L(language, "Framework", "Framework"), context.Framework);
        AddRow(table, L(language, "Style", "Style"), context.ArchitectureStyle ?? "N/A");
    }

    private static void AddMetricsSection(Section section, AegisArchitectureReport report, ReportLanguage language)
    {
        section.AddParagraph(L(language, "High-Level Quantitative Metrics", "Indicateurs Quantitatifs Globaux")).Style = StyleNames.Heading1;
        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("6cm");
        table.AddColumn("12cm");

        AddRow(table, L(language, "Files Scanned", "Fichiers Analysés"), report.TotalFilesScanned.ToString());
        AddRow(table, L(language, "Total Violations", "Violations Totales"), report.TotalViolations.ToString());
    }

    private static void AddComplianceMatrix(Section section, AegisArchitectureReport report, ReportLanguage language)
    {
        section.AddParagraph(L(language, "Compliance Index Matrix", "Matrice des Indices de Conformité")).Style = StyleNames.Heading1;
        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("8cm");
        table.AddColumn("5cm");
        table.AddColumn("5cm");

        var header = table.AddRow();
        ApplyRowStyle(header, true, false);
        header.Cells[0].AddParagraph(L(language, "Category", "Catégorie"));
        header.Cells[1].AddParagraph(L(language, "Score", "Score"));
        header.Cells[2].AddParagraph(L(language, "Status", "Statut"));

        bool alt = false;
        foreach (var score in report.ComplianceScores)
        {
            var row = table.AddRow();
            ApplyRowStyle(row, false, alt);
            row.Cells[0].AddParagraph(LocalizeCategory(score.Key.ToString(), language));
            row.Cells[1].AddParagraph($"{score.Value:0.00}%");
            row.Cells[2].AddParagraph(score.Value >= 80
                ? L(language, "PASSED", "CONFORME")
                : L(language, "WARNING", "ATTENTION"));
            alt = !alt;
        }
    }

    /// <summary>
    /// Renders every violation in the report, ranked by weighted impact.
    /// No cap: a compliance-grade PDF must reflect the full finding set, not a top-N excerpt.
    /// </summary>
    private static void AddViolationsLedger(Section section, AegisArchitectureReport report, ReportLanguage language)
    {
        section.AddParagraph(
            $"{L(language, "All Violations", "Toutes les Violations")} ({report.Results.Count})")
            .Style = StyleNames.Heading1;

        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("4cm");
        table.AddColumn("12cm");

        var header = table.AddRow();
        ApplyRowStyle(header, true, false);
        header.Cells[0].AddParagraph(L(language, "File", "Fichier"));
        header.Cells[1].AddParagraph(L(language, "Rule / Message", "Règle / Message"));

        var ordered = report.Results.OrderByDescending(x => x.WeightedImpact);
        bool alt = false;
        foreach (var item in ordered)
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

    private static void AddLayerAnalysisDetails(Section section, AegisArchitectureReport report, ReportLanguage language)
    {
        section.AddParagraph(L(language, "Layer Cross-Section Analysis", "Analyse Transversale par Couche")).Style = StyleNames.Heading1;
        foreach (var group in report.Results.GroupBy(x => x.Domain))
        {
            section.AddParagraph($"{L(language, "Layer", "Couche")}: {group.Key ?? "Core"}").Style = StyleNames.Heading2;
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

    private static void AddFooter(Section section, ReportLanguage language)
    {
        var culture = language == ReportLanguage.French
            ? new CultureInfo("fr-FR")
            : new CultureInfo("en-US");

        var f = section.Footers.Primary.AddParagraph();
        f.Format.Alignment = ParagraphAlignment.Center;
        f.AddText(
            $"{L(language, "Generated", "Généré le")} {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", culture)} UTC • Aegis v1.0");
    }
}