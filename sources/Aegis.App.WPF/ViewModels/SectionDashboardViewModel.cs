using System.Collections.ObjectModel;

using Aegis.Wpf.Models;
using Aegis.Shared.Architecture.Models;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;


namespace Aegis.Wpf.ViewModels;

public sealed partial class SectionDashboardViewModel : ObservableObject
{
    public SectionDashboardViewModel()
    {
        SectionResults =
            new ObservableCollection<SectionDashboardItem>();
    }



    // ==========================================================
    // PROPERTIES
    // ==========================================================

    [ObservableProperty]
    private string title =
        "🧩 Architecture Sections";



    public ObservableCollection<SectionDashboardItem> SectionResults
    {
        get;
    }



    [ObservableProperty]
    private int totalSections;



    [ObservableProperty]
    private int compliantCount;



    [ObservableProperty]
    private int nonCompliantCount;



    [ObservableProperty]
    private double averageScore;



    [ObservableProperty]
    private double projectHealth;



    [ObservableProperty]
    private ISeries[] compliancePieSeries = [];



    [ObservableProperty]
    private ISeries[] sectionCategorySeries = [];



    [ObservableProperty]
    private Axis[] categoryAxes = [];



    [ObservableProperty]
    private Axis[] valueAxes = [];



    // ==========================================================
    // UPDATE FROM REPORT
    // ==========================================================

    public void Update(
        AegisArchitectureReport report)
    {
        SectionResults.Clear();



        /*
         * ComplianceScores already contains every registered category.
         *
         * Example:
         *
         * Architecture   100
         * Dependency      95
         * Security        100
         * Performance     82
         *
         * This prevents clean categories from disappearing.
         */


        foreach (var section in report.ComplianceScores)
        {
            var findings =
                report.Results.Count(x =>
                    x.Category == section.Key);



            SectionResults.Add(
                new SectionDashboardItem(
                    section.Key.ToString(),
                    section.Key,
                    section.Value / 100.0,
                    findings,
                    section.Value >= 80
                        ? "Compliant"
                        : "Non-Compliant",
                    findings == 0
                        ? "No findings"
                        : $"{findings} findings"));
        }



        TotalSections =
            SectionResults.Count;



        CompliantCount =
            SectionResults.Count(x =>
                x.Status == "Compliant");



        NonCompliantCount =
            SectionResults.Count(x =>
                x.Status != "Compliant");



        AverageScore =
            SectionResults.Count == 0
                ? 100
                : SectionResults.Average(x =>
                    x.Score * 100);



        ProjectHealth =
            report.Metrics.ProjectHealthIndex;



        BuildComplianceChart();

        BuildCategoryChart();



    }



    // ==========================================================
    // CHARTS
    // ==========================================================

    private void BuildComplianceChart()
    {
        CompliancePieSeries =
        [
            new PieSeries<int>
            {
                Values =
                [
                    CompliantCount
                ],

                Name =
                    "Compliant",

                Fill =
                    new SolidColorPaint(
                        SKColors.LightGreen)
            },

            new PieSeries<int>
            {
                Values =
                [
                    NonCompliantCount
                ],

                Name =
                    "Non-Compliant",

                Fill =
                    new SolidColorPaint(
                        SKColors.IndianRed)
            }
        ];
    }



    private void BuildCategoryChart()
    {
        var data =
            SectionResults
                .OrderBy(x => x.Category.ToString())
                .Select(x => new
                {
                    Category =
                        x.Category.ToString(),

                    Score =
                        x.Score * 100
                })
                .ToList();



        SectionCategorySeries =
        [
            new ColumnSeries<double>
            {
                Values =
                    data
                        .Select(x =>
                            x.Score)
                        .ToArray(),

                Name =
                    "Compliance (%)",

                Fill =
                    new SolidColorPaint(
                        SKColors.DeepSkyBlue),

                Padding = 10
            }
        ];



        CategoryAxes =
        [
            new Axis
            {
                Labels =
                    data
                        .Select(x =>
                            x.Category)
                        .ToArray(),

                LabelsRotation = 15
            }
        ];



        ValueAxes =
        [
            new Axis
            {
                Name =
                    "Compliance (%)",

                MinLimit = 0,

                MaxLimit = 100,

                MinStep = 1
            }
        ];
    }
}