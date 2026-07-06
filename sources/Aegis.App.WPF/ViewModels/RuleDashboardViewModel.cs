using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class RuleDashboardViewModel : ObservableObject
{
    private readonly IRuleResultRepository _ruleRepo;
    private readonly IReportRepository _reportRepo;
    private readonly ILogger<RuleDashboardViewModel> _logger;
 
    public RuleDashboardViewModel(
        IRuleResultRepository ruleRepo,
        IReportRepository reportRepo,
        ILogger<RuleDashboardViewModel> logger)
    {
        _ruleRepo = ruleRepo;
        _reportRepo = reportRepo;
        _logger = logger;

        RuleResults = new ObservableCollection<RuleResultEntity>();

        LoadCommand = new AsyncRelayCommand(LoadAsync);
    }

    // =========================
    // COMMANDS
    // =========================
    public IAsyncRelayCommand LoadCommand { get; }

    // =========================
    // DATA GRID
    // =========================
    public ObservableCollection<RuleResultEntity> RuleResults { get; }

    // =========================
    // KPI
    // =========================
    [ObservableProperty] private int totalViolations;
    [ObservableProperty] private int criticalCount;
    [ObservableProperty] private int blockerCount;

    // =========================
    // CHARTS (MATCH XAML)
    // =========================
    [ObservableProperty] private ISeries[] severitySeries = [];
    [ObservableProperty]
    private ISeries[] categorySeries = [];


    [ObservableProperty] private Axis[] categoryAxes = [];
    [ObservableProperty] private Axis[] valueAxes = [];

    // =========================
    // LOAD
    // =========================
    private async Task LoadAsync()
    {
        try
        {
            _logger.LogInformation("Loading rule dashboard...");

            var reports = await _reportRepo.GetAllReportsAsync(default);
            var last = reports.OrderByDescending(r => r.ScanDate).FirstOrDefault();

            if (last is null)
                return;

            var violations = await _ruleRepo.GetViolationsByReportIdAsync(last.Id, default);

            RuleResults.Clear();

            foreach (var v in violations)
                RuleResults.Add(v);

            TotalViolations = RuleResults.Count;
            CriticalCount = RuleResults.Count(v => v.Severity == ArchitectureRuleSeverity.Critical);
            BlockerCount = RuleResults.Count(v => v.Severity == ArchitectureRuleSeverity.Blocker);

            BuildSeverityChart();
            BuildCategoryChart();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule dashboard load failed");
        }
    }

    // =========================
    // PIE
    // =========================
    private void BuildSeverityChart()
    {
        var grouped = RuleResults
            .GroupBy(v => v.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToList();

        SeveritySeries = grouped.Select(g =>
        {
            var color = g.Severity switch
            {
                ArchitectureRuleSeverity.Blocker => SKColors.DarkRed,
                ArchitectureRuleSeverity.Critical => SKColors.IndianRed,
                ArchitectureRuleSeverity.High => SKColors.Orange,
                ArchitectureRuleSeverity.Medium => SKColors.Gold,
                ArchitectureRuleSeverity.Info => SKColors.SkyBlue,
                _ => SKColors.Gray
            };

            return new PieSeries<int>
            {
                Values = new[] { g.Count },
                Name = g.Severity.ToString(),
                Fill = new SolidColorPaint(color)
            };
        }).ToArray();
    }

    // =========================
    // BAR
    // =========================
    private void BuildCategoryChart()
    {
        var grouped = RuleResults
            .GroupBy(v => v.Category)
            .Select(g => new
            {
                Category = g.Key ?? "Unknown",
                Count = g.Count()
            })
            .ToList();

        CategorySeries = new ISeries[]
        {
            new ColumnSeries<int>
            {
                Values = grouped.Select(x => x.Count).ToArray(),
                Name = "Violations",
                Fill = new SolidColorPaint(SKColors.DeepSkyBlue)
            }
        };

        CategoryAxes = new[]
        {
            new Axis
            {
                Labels = grouped.Select(x => x.Category).ToArray(),
                LabelsRotation = 15
            }
        };

        ValueAxes = new[]
        {
            new Axis
            {
                Name = "Count"
            }
        };
    }
}

// helper model unchanged
public sealed record LayerStat(string Layer, int Count);