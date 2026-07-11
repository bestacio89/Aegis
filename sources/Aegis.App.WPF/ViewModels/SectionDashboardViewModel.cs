using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Aegis.App.Wpf.Models;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class SectionDashboardViewModel : ObservableObject
{
    public SectionDashboardViewModel()
    {
        SectionResults = new ObservableCollection<SectionDashboardItem>();
    }

    // =========================
    // PROPERTIES
    // =========================

    [ObservableProperty]
    private string title = "🧩 Architecture Sections";

    public ObservableCollection<SectionDashboardItem> SectionResults { get; }

    [ObservableProperty]
    private int totalSections;

    [ObservableProperty]
    private int compliantCount;

    [ObservableProperty]
    private int nonCompliantCount;

    [ObservableProperty]
    private double averageScore;

    [ObservableProperty]
    private ISeries[] compliancePieSeries = [];

    [ObservableProperty]
    private ISeries[] sectionCategorySeries = [];

    [ObservableProperty]
    private Axis[] categoryAxes = [];

    [ObservableProperty]
    private Axis[] valueAxes = [];

    // =========================
    // UPDATE LOGIC
    // =========================

    public void Update(IReadOnlyCollection<SectionDashboardItem> sections)
    {
        SectionResults.Clear();
        foreach (var section in sections)
            SectionResults.Add(section);

        TotalSections = SectionResults.Count;
        CompliantCount = SectionResults.Count(x => x.Status == "Compliant");
        NonCompliantCount = SectionResults.Count(x => x.Status != "Compliant");
        AverageScore = SectionResults.Count == 0 ? 0 : SectionResults.Average(x => x.Score);

        BuildComplianceChart();
        BuildCategoryChart();
    }

    // =========================
    // CHARTS
    // =========================

    private void BuildComplianceChart()
    {
        CompliancePieSeries =
        [
            new PieSeries<int>
            {
                Values = [CompliantCount],
                Name = "Compliant",
                Fill = new SolidColorPaint(SKColors.LightGreen)
            },
            new PieSeries<int>
            {
                Values = [NonCompliantCount],
                Name = "Non-Compliant",
                Fill = new SolidColorPaint(SKColors.IndianRed)
            }
        ];
    }

    private void BuildCategoryChart()
    {
        // Using double for scores to represent percentages accurately
        var data = SectionResults
            .GroupBy(x => x.Category)
            .Select(g => new
            {
                Category = g.Key.ToString(),
                AverageScore = g.Average(item => (double)item.Score)
            })
            .OrderBy(x => x.Category)
            .ToList();

        SectionCategorySeries =
        [
            new ColumnSeries<double>
            {
                Values = data.Select(x => x.AverageScore).ToArray(),
                Name = "Average Score (%)",
                Fill = new SolidColorPaint(SKColors.DeepSkyBlue),
                Padding = 10
            }
        ];

        CategoryAxes =
        [
            new Axis
            {
                Labels = data.Select(x => x.Category).ToArray(),
                LabelsRotation = 15,
                SeparatorsPaint = null
            }
        ];

        ValueAxes =
        [
            new Axis
            {
                Name = "Score (%)",
                MinLimit = 0,
                MaxLimit = 100,
                ForceStepToMin = true,
                MinStep = 1
            }
        ];
    }
}