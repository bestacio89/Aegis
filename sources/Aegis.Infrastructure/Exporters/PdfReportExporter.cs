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
/// 📄 Executive/Technical PDF Report Exporter using MigraDoc.
///
/// Structured as a genuine architecture audit document, not a flat data dump:
/// executive narrative → architecture profile → compliance by category → compliance
/// by layer (real project/module names, not a generic "General" bucket) → findings
/// grouped by layer then severity, using the now-correct per-violation FilePath/
/// Target/Namespace/Domain fields.
///
/// Always renders full detail — a PDF is the situational record of everything found,
/// regardless of the requested detailLevel. detailLevel is accepted for interface
/// compatibility but intentionally not used to truncate content here.
/// </summary>
public sealed class PdfReportExporter : IReportExporter
{
    public string Format => "pdf";

    private readonly ILogger<PdfReportExporter> _logger;
    private static bool _fontResolverRegistered;
    private static readonly object _fontResolverLock = new();

    private static readonly Color PrimaryAccentColor = new(41, 128, 185);    // Architectural Blue
    private static readonly Color SecondaryAccentColor = new(52, 73, 94);    // Dark Slate
    private static readonly Color LightRowBgColor = new(244, 246, 249);      // Soft Zebra
    private static readonly Color BorderColor = new(200, 200, 200);         // Neutral Border
    private static readonly Color HealthyColor = new(39, 174, 96);           // Green
    private static readonly Color WarningColor = new(230, 162, 61);          // Amber
    private static readonly Color CriticalColor = new(197, 48, 48);          // Red

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

    private static Color HealthColor(double scoreOutOf100) =>
        scoreOutOf100 >= 80 ? HealthyColor
        : scoreOutOf100 >= 50 ? WarningColor
        : CriticalColor;

    private static Color SeverityColor(ArchitectureRuleSeverity severity) => severity switch
    {
        ArchitectureRuleSeverity.Blocker => CriticalColor,
        ArchitectureRuleSeverity.Critical => CriticalColor,
        ArchitectureRuleSeverity.High => WarningColor,
        ArchitectureRuleSeverity.Medium => WarningColor,
        _ => SecondaryAccentColor
    };

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
        AddExecutiveNarrative(section, report, language);
        AddProjectSummaryCard(section, report, context, language);
        AddArchitectureProfile(section, context, language);
        AddComplianceMatrix(section, report, language);
        AddLayerComplianceMatrix(section, report, language);
        AddFindingsByLayer(section, report, language);
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

    /// <summary>
    /// One-paragraph plain-language status before any table — the reader should know
    /// whether the project is healthy, at-risk, or critical before seeing a single number.
    /// </summary>
    private static void AddExecutiveNarrative(Section section, AegisArchitectureReport report, ReportLanguage language)
    {
        section.AddParagraph(L(language, "Executive Summary", "Synthèse Exécutive")).Style = StyleNames.Heading1;

        var health = report.Metrics?.ProjectHealthIndex ?? 0;

        var status = health >= 80
            ? L(language, "healthy", "saine")
            : health >= 50
                ? L(language, "at risk", "à risque")
                : L(language, "critical", "critique");

        var narrative = L(
            language,
            $"{report.ProjectName} was scanned across {report.TotalFilesScanned} files and evaluated against " +
            $"{report.ComplianceScores.Count} rule categories. The overall architectural health is {status} " +
            $"at {health:0.##}%, with {report.TotalViolations} findings identified across the scanned codebase.",
            $"{report.ProjectName} a été analysé sur {report.TotalFilesScanned} fichiers et évalué selon " +
            $"{report.ComplianceScores.Count} catégories de règles. La santé architecturale globale est {status} " +
            $"à {health:0.##}%, avec {report.TotalViolations} constats identifiés dans le code analysé.");

        var p = section.AddParagraph(narrative);
        p.Format.SpaceAfter = 10;
    }

    private static void AddProjectSummaryCard(Section section, AegisArchitectureReport report, ProjectArchitectureContext context, ReportLanguage language)
    {
        section.AddParagraph(L(language, "Project Identity", "Identité du Projet")).Style = StyleNames.Heading1;
        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("4.5cm");
        table.AddColumn("13.5cm");

        AddRow(table, L(language, "Target Project", "Projet Cible"), report.ProjectName, true);
        AddRow(table, L(language, "Workspace Path", "Chemin du Projet"), report.ProjectPath);
        AddRow(table, L(language, "Language", "Langage"), context.Language);
        AddRow(table, L(language, "Framework", "Framework"), context.Framework ?? "N/A");
        AddRow(table, L(language, "Architecture Style", "Style Architectural"), context.ArchitectureStyle ?? "N/A");
        AddRow(table, L(language, "Scan Date (UTC)", "Date d'Analyse (UTC)"), report.ScanDate.ToString("yyyy-MM-dd HH:mm"));
    }

    /// <summary>
    /// New: leverages the enriched ProjectArchitectureContext (Modules, Layers, detected
    /// patterns, build system, detection confidence) — this is what makes the PDF read as
    /// a technical architecture audit rather than a bare violation dump.
    /// </summary>
    private static void AddArchitectureProfile(Section section, ProjectArchitectureContext context, ReportLanguage language)
    {
        section.AddParagraph(L(language, "Architecture Profile", "Profil Architectural")).Style = StyleNames.Heading1;

        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("6cm");
        table.AddColumn("12cm");

        AddRow(table, L(language, "Modules Detected", "Modules Détectés"), context.Modules.Count.ToString());
        AddRow(table, L(language, "Layers Detected", "Couches Détectées"), context.Layers.Count.ToString());
        AddRow(table, L(language, "Build System", "Système de Build"), context.BuildSystem.ToString());
        AddRow(table, L(language, "Detection Confidence", "Confiance de Détection"), $"{context.Confidence:P0}");
        AddRow(table, L(language, "Detector Version", "Version du Détecteur"), context.DetectorVersion);

        if (context.PatternsDetected.Count > 0)
        {
            AddRow(table, L(language, "Patterns Detected", "Modèles Détectés"), string.Join(", ", context.PatternsDetected));
        }

        if (context.Modules.Count > 0)
        {
            section.AddParagraph(L(language, "Detected Modules", "Modules Détectés")).Style = StyleNames.Heading2;

            var modTable = section.AddTable();
            ConfigureBaseTable(modTable);
            modTable.AddColumn("6cm");
            modTable.AddColumn("6cm");
            modTable.AddColumn("6cm");

            var header = modTable.AddRow();
            ApplyRowStyle(header, true, false);
            header.Cells[0].AddParagraph(L(language, "Module", "Module"));
            header.Cells[1].AddParagraph(L(language, "Type", "Type"));
            header.Cells[2].AddParagraph(L(language, "Languages", "Langages"));

            bool alt = false;
            foreach (var module in context.Modules.OrderBy(m => m.Name))
            {
                var row = modTable.AddRow();
                ApplyRowStyle(row, false, alt);
                row.Cells[0].AddParagraph(module.Name);
                row.Cells[1].AddParagraph(module.Type ?? "Unknown");
                row.Cells[2].AddParagraph(module.Languages.Count > 0 ? string.Join(", ", module.Languages) : "N/A");
                alt = !alt;
            }
        }
    }

    private static void AddComplianceMatrix(Section section, AegisArchitectureReport report, ReportLanguage language)
    {
        section.AddParagraph(L(language, "Compliance by Category", "Conformité par Catégorie")).Style = StyleNames.Heading1;
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
        foreach (var score in report.ComplianceScores.OrderByDescending(x => x.Value))
        {
            var row = table.AddRow();
            ApplyRowStyle(row, false, alt);
            row.Cells[0].AddParagraph(LocalizeCategory(score.Key.ToString(), language));

            var scoreCell = row.Cells[1].AddParagraph($"{score.Value:0.00}%");
            scoreCell.Format.Font.Color = HealthColor(score.Value);
            scoreCell.Format.Font.Bold = true;

            row.Cells[2].AddParagraph(score.Value >= 80
                ? L(language, "PASSED", "CONFORME")
                : score.Value >= 50
                    ? L(language, "WARNING", "ATTENTION")
                    : L(language, "CRITICAL", "CRITIQUE"));
            alt = !alt;
        }
    }

    /// <summary>
    /// New: compliance broken down by real layer/module (Aegis.Core, Aegis.Shared, etc.)
    /// rather than only by rule category. report.Domains now has full coverage — every
    /// detected layer appears here, including layers with zero findings — and
    /// HealthIndex/WeightedScore are stored as 0-1 fractions, so both are scaled to a
    /// 0-100 percentage here for display (same convention fixed in the WPF layer view).
    /// </summary>
    private static void AddLayerComplianceMatrix(Section section, AegisArchitectureReport report, ReportLanguage language)
    {
        section.AddParagraph(L(language, "Compliance by Layer", "Conformité par Couche")).Style = StyleNames.Heading1;

        if (report.Domains.Count == 0)
        {
            section.AddParagraph(L(language, "No layer data available for this scan.", "Aucune donnée de couche disponible pour cette analyse."));
            return;
        }

        var table = section.AddTable();
        ConfigureBaseTable(table);
        table.AddColumn("6cm");
        table.AddColumn("3cm");
        table.AddColumn("3cm");
        table.AddColumn("3cm");
        table.AddColumn("3cm");

        var header = table.AddRow();
        ApplyRowStyle(header, true, false);
        header.Cells[0].AddParagraph(L(language, "Layer / Module", "Couche / Module"));
        header.Cells[1].AddParagraph(L(language, "Rules Evaluated", "Règles Évaluées"));
        header.Cells[2].AddParagraph(L(language, "Violations", "Violations"));
        header.Cells[3].AddParagraph(L(language, "Health", "Santé"));
        header.Cells[4].AddParagraph(L(language, "Compliance", "Conformité"));

        bool alt = false;
        foreach (var domain in report.Domains.OrderByDescending(d => d.Violations))
        {
            var health = domain.HealthIndex * 100;
            var compliance = domain.WeightedScore * 100;

            var row = table.AddRow();
            ApplyRowStyle(row, false, alt);
            row.Cells[0].AddParagraph(domain.Domain);
            row.Cells[1].AddParagraph(domain.RulesEvaluated.ToString());
            row.Cells[2].AddParagraph(domain.Violations.ToString());

            var healthCell = row.Cells[3].AddParagraph($"{health:0.0}%");
            healthCell.Format.Font.Color = HealthColor(health);
            healthCell.Format.Font.Bold = true;

            row.Cells[4].AddParagraph($"{compliance:0.0}%");
            alt = !alt;
        }
    }

    /// <summary>
    /// Replaces the previous flat "All Violations" ledger plus a separate, redundant
    /// per-layer dump with one findings section: grouped by real layer, ordered by
    /// severity within each layer, using the now-correctly-populated FilePath/Target/
    /// Namespace fields (previously Target was always empty and Namespace held the
    /// evaluator's display name instead of an actual namespace).
    /// </summary>
    private static void AddFindingsByLayer(Section section, AegisArchitectureReport report, ReportLanguage language)
    {
        section.AddParagraph(
            $"{L(language, "Findings by Layer", "Constats par Couche")} ({report.Results.Count})")
            .Style = StyleNames.Heading1;

        if (report.Results.Count == 0)
        {
            section.AddParagraph(L(language, "No findings were recorded for this scan.", "Aucun constat enregistré pour cette analyse."));
            return;
        }

        foreach (var layerGroup in report.Results
                     .GroupBy(x => string.IsNullOrWhiteSpace(x.Domain) ? "Unclassified" : x.Domain)
                     .OrderByDescending(g => g.Count()))
        {
            section.AddParagraph($"{layerGroup.Key} ({layerGroup.Count()})").Style = StyleNames.Heading2;

            var table = section.AddTable();
            ConfigureBaseTable(table);
            table.AddColumn("2.5cm");
            table.AddColumn("4cm");
            table.AddColumn("3.5cm");
            table.AddColumn("8cm");

            var header = table.AddRow();
            ApplyRowStyle(header, true, false);
            header.Cells[0].AddParagraph(L(language, "Severity", "Sévérité"));
            header.Cells[1].AddParagraph(L(language, "File", "Fichier"));
            header.Cells[2].AddParagraph(L(language, "Target", "Cible"));
            header.Cells[3].AddParagraph(L(language, "Rule / Message", "Règle / Message"));

            bool alt = false;
            foreach (var item in layerGroup.OrderByDescending(x => x.Severity).ThenByDescending(x => x.WeightedImpact))
            {
                var row = table.AddRow();
                ApplyRowStyle(row, false, alt);

                var sevCell = row.Cells[0].AddParagraph(item.Severity.ToString());
                sevCell.Format.Font.Color = SeverityColor(item.Severity);
                sevCell.Format.Font.Bold = true;

                row.Cells[1].AddParagraph(string.IsNullOrWhiteSpace(item.FilePath)
                    ? "N/A"
                    : Path.GetFileName(item.FilePath));

                row.Cells[2].AddParagraph(string.IsNullOrWhiteSpace(item.Target) ? "—" : item.Target);

                var p = row.Cells[3].AddParagraph();
                p.AddFormattedText($"{item.RuleName}: ", TextFormat.Bold);
                p.AddText(item.Message);

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