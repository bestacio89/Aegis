using Aegis.App.Wpf.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class SectionDashboardViewModel
    : ObservableObject
{
    public SectionDashboardViewModel()
    {
        SectionResults =
            new ObservableCollection<SectionDashboardItem>();
    }


    // =========================
    // HEADER
    // =========================

    [ObservableProperty]
    private string title =
        "🧩 Architecture Sections";



    // =========================
    // GRID
    // =========================

    public ObservableCollection<SectionDashboardItem>
        SectionResults
    { get; }



    // =========================
    // KPI
    // =========================

    [ObservableProperty]
    private int totalSections;


    [ObservableProperty]
    private int compliantCount;


    [ObservableProperty]
    private int nonCompliantCount;


    [ObservableProperty]
    private double averageScore;



    // =========================
    // CHARTS
    // =========================

    [ObservableProperty]
    private ISeries[] compliancePieSeries = [];


    [ObservableProperty]
    private ISeries[] sectionCategorySeries = [];


    [ObservableProperty]
    private Axis[] categoryAxes = [];


    [ObservableProperty]
    private Axis[] valueAxes = [];



    // =========================
    // UPDATE
    // =========================

    public void Update(
        IReadOnlyCollection<SectionDashboardItem> sections)
    {
        SectionResults.Clear();


        foreach (var section in sections)
            SectionResults.Add(section);



        TotalSections =
            SectionResults.Count;



        CompliantCount =
            SectionResults.Count(x =>
                x.Status == "Compliant");



        NonCompliantCount =
            SectionResults.Count(x =>
                x.Status != "Compliant");



        averageScore =
            SectionResults.Count == 0
                ? 0
                : SectionResults.Average(x => x.Score);



        BuildComplianceChart();

        BuildCategoryChart();
    }



    // =========================
    // PIE
    // =========================

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

                Name = "Compliant",

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

                Name = "Non-Compliant",

                Fill =
                    new SolidColorPaint(
                        SKColors.IndianRed)
            }
        ];
    }



    // =========================
    // BAR
    // =========================

    private void BuildCategoryChart()
    {
        var grouped =
            SectionResults
                .GroupBy(x => x.Category)
                .Select(x => new
                {
                    Category = x.Key,
                    Count = x.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToList();



        SectionCategorySeries =
        [
            new ColumnSeries<int>
            {
                Values =
                    grouped
                        .Select(x => x.Count)
                        .ToArray(),

                Name = "Sections",

                Fill =
                    new SolidColorPaint(
                        SKColors.DeepSkyBlue)
            }
        ];



        CategoryAxes =
        [
            new Axis
            {
                Labels =
                    grouped
                        .Select(x => x.Category.ToString())
                        .ToArray(),

                LabelsRotation = 15
            }
        ];



        ValueAxes =
        [
            new Axis
            {
                Name = "Count"
            }
        ];
    }
}