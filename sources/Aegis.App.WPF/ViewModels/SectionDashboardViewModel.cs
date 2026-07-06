using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class SectionDashboardViewModel : ObservableObject
{
    // =========================
    // HEADER
    // =========================
    [ObservableProperty]
    private string title = "🧩 Aegis Policy Sections";

    // =========================
    // DATA GRID
    // =========================
    [ObservableProperty]
    private ObservableCollection<SectionResult> sectionResults = new();

    // =========================
    // KPI (MATCH XAML)
    // =========================
    [ObservableProperty] private int totalSections;
    [ObservableProperty] private int compliantCount;
    [ObservableProperty] private int nonCompliantCount;

    // =========================
    // CHARTS (MATCH XAML)
    // =========================
    [ObservableProperty] private ISeries[] compliancePieSeries = [];
    [ObservableProperty] private ISeries[] sectionCategorySeries = [];

    [ObservableProperty] private Axis[] categoryAxes = [];
    [ObservableProperty] private Axis[] valueAxes = [];

    public SectionDashboardViewModel()
    {
        LoadPolicySections();
    }

    // =========================
    // LOAD
    // =========================
    private void LoadPolicySections()
    {
        try
        {
            var policyPath = Path.Combine(AppContext.BaseDirectory, "config", "aegis.policy.json");

            if (!File.Exists(policyPath))
            {
                BuildEmpty("Policy file not found");
                return;
            }

            var json = File.ReadAllText(policyPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("AegisPolicy", out var root))
            {
                BuildEmpty("Invalid policy structure");
                return;
            }

            var sections = new List<SectionResult>();

            int compliant = 0;
            int nonCompliant = 0;

            var names = new List<string>();
            var counts = new List<int>();

            foreach (var prop in root.EnumerateObject())
            {
                var name = prop.Name;
                var ruleCount = prop.Value.EnumerateObject().Count();

                names.Add(name);
                counts.Add(ruleCount);

                var score = ruleCount > 5 ? 0.4 : 0.9;
                var status = score >= 0.7 ? "Compliant" : "Non-Compliant";

                if (status == "Compliant") compliant++;
                else nonCompliant++;

                sections.Add(new SectionResult(
                    SectionId: name.GetHashCode(),
                    SectionName: name,
                    ComplianceStatus: status,
                    Category: "Policy",
                    Score: score,
                    Remarks: $"{ruleCount} rules"
                ));
            }

            sectionResults = new ObservableCollection<SectionResult>(sections);

            totalSections = sections.Count;
            compliantCount = compliant;
            nonCompliantCount = nonCompliant;

            BuildBarChart(names, counts);
            BuildPieChart(compliant, nonCompliant);
        }
        catch
        {
            BuildEmpty("Failed to load policy data");
        }
    }

    // =========================
    // PIE CHART
    // =========================
    private void BuildPieChart(int compliant, int nonCompliant)
    {
        compliancePieSeries = new ISeries[]
        {
            new PieSeries<int> { Values = new[] { compliant }, Name = "Compliant", Fill = new SolidColorPaint(SKColors.LightGreen) },
            new PieSeries<int> { Values = new[] { nonCompliant }, Name = "Non-Compliant", Fill = new SolidColorPaint(SKColors.IndianRed) }
        };
    }

    // =========================
    // BAR CHART
    // =========================
    private void BuildBarChart(List<string> names, List<int> counts)
    {
        sectionCategorySeries = new ISeries[]
        {
            new ColumnSeries<int>
            {
                Values = counts,
                Name = "Rules",
                Fill = new SolidColorPaint(new SKColor(0, 191, 255))
            }
        };

        categoryAxes = new[]
        {
            new Axis
            {
                Labels = names,
                LabelsRotation = 15
            }
        };

        valueAxes = new[]
        {
            new Axis
            {
                Name = "Rule Count"
            }
        };
    }

    // =========================
    // EMPTY STATE
    // =========================
    private void BuildEmpty(string message)
    {
        Title = message;

        sectionResults.Clear();

        totalSections = 0;
        compliantCount = 0;
        nonCompliantCount = 0;

        compliancePieSeries = [];
        sectionCategorySeries = [];

        categoryAxes = [];
        valueAxes = [];
    }
}

// =========================
// MODEL (must exist somewhere shared)
// =========================
public sealed record SectionResult(
    int SectionId,
    string SectionName,
    string ComplianceStatus,
    string Category,
    double Score,
    string Remarks
);