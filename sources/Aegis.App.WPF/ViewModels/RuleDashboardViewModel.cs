using Aegis.Wpf.models;
using Aegis.Wpf.Models;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using Microsoft.Extensions.Logging;

using SkiaSharp;

using System.Collections.ObjectModel;
using System.IO;

namespace Aegis.Wpf.ViewModels;

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



    // ==========================================================
    // GRID
    // ==========================================================

    public ObservableCollection<RuleDashboardItem> RuleResults { get; }



    // ==========================================================
    // KPI
    // ==========================================================

    [ObservableProperty]
    private int totalViolations;


    [ObservableProperty]
    private int blockerCount;


    [ObservableProperty]
    private int criticalCount;


    [ObservableProperty]
    private int highCount;


    [ObservableProperty]
    private string mostAffectedCategory = "-";


    [ObservableProperty]
    private double projectHealth;



    [ObservableProperty]
    private double weightedCompliance;



    // ==========================================================
    // CHARTS
    // ==========================================================

    [ObservableProperty]
    private ISeries[] severitySeries = [];


    [ObservableProperty]
    private ISeries[] categorySeries = [];


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
        try
        {
            RuleResults.Clear();


            foreach (var result in report.Results)
            {
                RuleResults.Add(
                    new RuleDashboardItem(
                        result.RuleName,
                        result.Category,
                        result.Severity,
                        Path.GetFileName(result.Target),
                        result.Message,
                        result.WeightedImpact));
            }



            TotalViolations =
                report.TotalViolations;



            BlockerCount =
                report.Results.Count(x =>
                    x.Severity ==
                    ArchitectureRuleSeverity.Blocker);



            CriticalCount =
                report.Results.Count(x =>
                    x.Severity ==
                    ArchitectureRuleSeverity.Critical);



            HighCount =
                report.Results.Count(x =>
                    x.Severity ==
                    ArchitectureRuleSeverity.High);



            MostAffectedCategory =
                report.Results
                    .GroupBy(x => x.Category.ToString())
                    .OrderByDescending(x => x.Count())
                    .FirstOrDefault()
                    ?.Key
                    ?? "-";



            ProjectHealth =
                report.Metrics.ProjectHealthIndex;



            WeightedCompliance =
                report.Metrics.ProjectHealthIndex;



            BuildSeverityChart();

            BuildCategoryChart(report);



            _logger.LogInformation(
                "Rule dashboard updated from report. {Rules} findings.",
                RuleResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed updating rule dashboard.");
        }
    }



    // ==========================================================
    // CHART BUILDERS
    // ==========================================================

    private void BuildSeverityChart()
    {
        var grouped =
            Enum.GetValues<ArchitectureRuleSeverity>()
                .Select(severity =>
                    new
                    {
                        Severity = severity,

                        Count =
                            RuleResults.Count(x =>
                                x.Severity == severity)
                    })
                .ToList();



        SeveritySeries =
            grouped.Select(x =>
            {
                return new PieSeries<int>
                {
                    Values =
                    [
                        x.Count
                    ],

                    Name =
                        x.Severity.ToString(),

                    Fill =
                        new SolidColorPaint(
                            GetSeverityColor(x.Severity))
                };

            })
            .ToArray();
    }



    private void BuildCategoryChart(
        AegisArchitectureReport report)
    {
        /*
         * Use ComplianceScores as the source of truth.
         *
         * This guarantees that clean categories appear:
         *
         * Security       100%
         * Architecture   95%
         * Dependency     80%
         *
         * instead of only showing categories
         * where violations happened.
         */


        var categories =
            report.ComplianceScores
                .OrderBy(x => x.Value)
                .ToList();



        CategorySeries =
        [
            new ColumnSeries<double>
            {
                Values =
                    categories
                        .Select(x => x.Value)
                        .ToArray(),

                Name =
                    "Compliance %",

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
                    categories
                        .Select(x =>
                            x.Key.ToString())
                        .ToArray(),

                LabelsRotation = 25
            }
        ];



        ValueAxes =
        [
            new Axis
            {
                Name =
                    "Compliance %",

                MinLimit = 0,

                MaxLimit = 100
            }
        ];
    }



    private static SKColor GetSeverityColor(
        ArchitectureRuleSeverity severity)
    {
        return severity switch
        {
            ArchitectureRuleSeverity.Blocker =>
                SKColors.DarkRed,

            ArchitectureRuleSeverity.Critical =>
                SKColors.IndianRed,

            ArchitectureRuleSeverity.High =>
                SKColors.Orange,

            ArchitectureRuleSeverity.Medium =>
                SKColors.Gold,

            ArchitectureRuleSeverity.Low =>
                SKColors.LightGreen,

            ArchitectureRuleSeverity.Info =>
                SKColors.SkyBlue,

            _ =>
                SKColors.Gray
        };
    }
}