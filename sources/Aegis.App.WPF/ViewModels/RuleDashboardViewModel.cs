using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Aegis.App.Wpf.ViewModels;

public sealed class RuleDashboardViewModel
{
    private readonly IRuleResultRepository _ruleRepo;
    private readonly IReportRepository _reportRepo;
    private readonly ILogger<RuleDashboardViewModel> _logger;

    public ObservableCollection<RuleResultEntity> RuleResults { get; } = new();

    // --- Chart bindings ---
    public PlotModel SeverityPlotModel { get; private set; } = new();
    public PlotModel CategoryPlotModel { get; private set; } = new();

    // --- KPI bindings ---
    public int TotalViolations { get; private set; }
    public int CriticalCount { get; private set; }
    public int BlockerCount { get; private set; }

    public RuleDashboardViewModel(
        IRuleResultRepository ruleRepo,
        IReportRepository reportRepo,
        ILogger<RuleDashboardViewModel> logger)
    {
        _ruleRepo = ruleRepo;
        _reportRepo = reportRepo;
        _logger = logger;

        _ = LoadAsync(); // fire & forget
    }

    private async Task LoadAsync()
    {
        try
        {
            _logger.LogInformation("📊 Loading latest report results...");
            var reports = await _reportRepo.GetAllReportsAsync(default);
            var lastReport = reports.OrderByDescending(r => r.ScanDate).FirstOrDefault();

            if (lastReport == null)
            {
                _logger.LogWarning("⚠️ No reports found.");
                return;
            }

            var violations = await _ruleRepo.GetViolationsByReportIdAsync(lastReport.Id, default);

            RuleResults.Clear();
            foreach (var v in violations)
                RuleResults.Add(v);

            TotalViolations = RuleResults.Count;
            CriticalCount = RuleResults.Count(v => v.Severity == RuleSeverity.Critical);
            BlockerCount = RuleResults.Count(v => v.Severity == RuleSeverity.Blocker);

            BuildSeverityChart();
            BuildCategoryChart();

            _logger.LogInformation("✅ Loaded {Count} rule violations.", RuleResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to load rule dashboard data.");
        }
    }

    // -------------------- OxyPlot builders --------------------

    private void BuildSeverityChart()
    {
        var grouped = RuleResults
            .GroupBy(v => v.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToList();

        var model = new PlotModel
        {
            Title = "Violations by Severity",
            TextColor = OxyColors.White,
            Background = OxyColor.FromRgb(30, 30, 30)
        };

        var pie = new PieSeries
        {
            StrokeThickness = 1,
            InsideLabelPosition = 0.8,
            AngleSpan = 360,
            StartAngle = 0,
            FontSize = 14
        };

        foreach (var g in grouped)
        {
            var color = g.Severity switch
            {
                RuleSeverity.Blocker => OxyColors.DarkRed,
                RuleSeverity.Critical => OxyColors.IndianRed,
                RuleSeverity.High => OxyColors.Orange,
                RuleSeverity.Medium => OxyColors.Gold,
                RuleSeverity.Info => OxyColors.SkyBlue,
                _ => OxyColors.Gray
            };

            pie.Slices.Add(new PieSlice(g.Severity.ToString(), g.Count) { Fill = color });
        }

        model.Series.Add(pie);
        SeverityPlotModel = model;
    }

    private void BuildCategoryChart()
    {
        var grouped = RuleResults
            .GroupBy(v => v.Category)
            .Select(g => new { Category = g.Key ?? "Unknown", Count = g.Count() })
            .ToList();

        var model = new PlotModel
        {
            Title = "Violations by Category",
            TextColor = OxyColors.White,
            Background = OxyColor.FromRgb(30, 30, 30)
        };

        // 🧭 Axes
        var catAxis = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            TextColor = OxyColors.White,
            Title = "Category"
        };

        var valAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Count",
            TextColor = OxyColors.White,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        };

        foreach (var g in grouped)
            catAxis.Labels.Add(g.Category);

        // 🧱 BarSeries (acts as column series when you flip axes)
        var barSeries = new BarSeries
        {
            FillColor = OxyColor.FromRgb(0, 191, 255),
            StrokeColor = OxyColors.White,
            StrokeThickness = 1,
            ItemsSource = grouped.Select(g => new BarItem { Value = g.Count }).ToList(),
            LabelPlacement = LabelPlacement.Inside,
            LabelFormatString = "{0}"
        };

        // For vertical “column” look, we flip the axes
        model.Axes.Add(valAxis);
        model.Axes.Add(catAxis);
        model.Series.Add(barSeries);

        CategoryPlotModel = model;
    }

}
