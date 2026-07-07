using Aegis.App.Wpf.models;
using Aegis.Shared.Architecture.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class RuleDashboardViewModel : ObservableObject
{
    private readonly ILogger<RuleDashboardViewModel> _logger;


    public RuleDashboardViewModel(
        ILogger<RuleDashboardViewModel> logger)
    {
        _logger = logger;

        RuleResults =
            new ObservableCollection<RuleDashboardItem>();
    }


    // =========================
    // GRID
    // =========================

    public ObservableCollection<RuleDashboardItem> RuleResults { get; }



    // =========================
    // KPI
    // =========================

    [ObservableProperty]
    private int totalViolations;


    [ObservableProperty]
    private int criticalCount;


    [ObservableProperty]
    private int blockerCount;


    [ObservableProperty]
    private int highCount;



    [ObservableProperty]
    private string mostAffectedCategory = "-";



    // =========================
    // CHARTS
    // =========================

    [ObservableProperty]
    private ISeries[] severitySeries = [];


    [ObservableProperty]
    private ISeries[] categorySeries = [];


    [ObservableProperty]
    private Axis[] categoryAxes = [];


    [ObservableProperty]
    private Axis[] valueAxes = [];



    // =========================
    // UPDATE FROM ANALYSIS
    // =========================

    public void Update(
        IReadOnlyCollection<RuleDashboardItem> rules)
    {
        try
        {
            RuleResults.Clear();


            foreach (var rule in rules)
                RuleResults.Add(rule);



            TotalViolations =
                RuleResults.Count;


            CriticalCount =
                RuleResults.Count(x =>
                    x.Severity ==
                    ArchitectureRuleSeverity.Critical);



            BlockerCount =
                RuleResults.Count(x =>
                    x.Severity ==
                    ArchitectureRuleSeverity.Blocker);



            HighCount =
                RuleResults.Count(x =>
                    x.Severity ==
                    ArchitectureRuleSeverity.High);



            MostAffectedCategory =
                RuleResults
                    .GroupBy(x => x.Category.ToString())
                    .OrderByDescending(x => x.Count())
                    .FirstOrDefault()
                    ?.Key
                    ?? "-";



            BuildSeverityChart();

            BuildCategoryChart();


            _logger.LogInformation(
                "Rule dashboard updated. {Count} rules.",
                TotalViolations);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Rule dashboard update failed.");
        }
    }



    private void BuildSeverityChart()
    {
        var grouped =
            RuleResults
                .GroupBy(x => x.Severity)
                .Select(x => new
                {
                    Severity = x.Key,
                    Count = x.Count()
                })
                .ToList();



        SeveritySeries =
            grouped.Select(x =>
            {
                var color = x.Severity switch
                {
                    ArchitectureRuleSeverity.Blocker
                        => SKColors.DarkRed,

                    ArchitectureRuleSeverity.Critical
                        => SKColors.IndianRed,

                    ArchitectureRuleSeverity.High
                        => SKColors.Orange,

                    ArchitectureRuleSeverity.Medium
                        => SKColors.Gold,

                    ArchitectureRuleSeverity.Info
                        => SKColors.SkyBlue,

                    _ => SKColors.Gray
                };


                return new PieSeries<int>
                {
                    Values =
                    [
                        x.Count
                    ],

                    Name =
                        x.Severity.ToString(),

                    Fill =
                        new SolidColorPaint(color)
                };

            }).ToArray();
    }



    private void BuildCategoryChart()
    {
        var grouped =
            RuleResults
                .GroupBy(x => x.Category)
                .Select(x => new
                {
                    Category = x.Key,
                    Count = x.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToList();



        CategorySeries =
        [
            new ColumnSeries<int>
            {
                Values =
                    grouped
                        .Select(x => x.Count)
                        .ToArray(),

                Name = "Violations",

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