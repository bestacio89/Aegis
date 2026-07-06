using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;

namespace Aegis.App.Wpf.ViewModels;

public sealed class RuleDashboardViewModel
{
    private readonly IRuleResultRepository _ruleRepo;
    private readonly IReportRepository _reportRepo;
    private readonly ILogger<RuleDashboardViewModel> _logger;

    public ObservableCollection<RuleResultEntity> RuleResults { get; } = new();

    // ---------------- LIVECHARTS BINDINGS ----------------

    public ISeries[] SeveritySeries { get; private set; } = Array.Empty<ISeries>();
    public ISeries[] CategorySeries { get; private set; } = Array.Empty<ISeries>();

    public Axis[] CategoryXAxis { get; private set; } = Array.Empty<Axis>();
    public Axis[] ValueYAxis { get; private set; } = Array.Empty<Axis>();

    // ---------------- KPIs ----------------

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

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _logger.LogInformation("Loading latest rule report...");

            var reports = await _reportRepo.GetAllReportsAsync(default);
            var lastReport = reports.OrderByDescending(r => r.ScanDate).FirstOrDefault();

            if (lastReport == null)
            {
                _logger.LogWarning("No reports found.");
                return;
            }

            var violations = await _ruleRepo.GetViolationsByReportIdAsync(lastReport.Id, default);

            RuleResults.Clear();
            foreach (var v in violations)
                RuleResults.Add(v);

            TotalViolations = RuleResults.Count;
            CriticalCount = RuleResults.Count(v => v.Severity == ArchitectureRuleSeverity.Critical);
            BlockerCount = RuleResults.Count(v => v.Severity == ArchitectureRuleSeverity.Blocker);

            BuildSeverityChart();
            BuildCategoryChart();

            _logger.LogInformation("Loaded {Count} violations", RuleResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed loading dashboard");
        }
    }

    // --------------------------------------------------
    // PIE CHART (Severity)
    // --------------------------------------------------

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

    // --------------------------------------------------
    // BAR / COLUMN CHART (Category)
    // --------------------------------------------------

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

        CategoryXAxis = new Axis[]
        {
            new Axis
            {
                Labels = grouped.Select(x => x.Category).ToArray(),
                LabelsRotation = 15
            }
        };

        ValueYAxis = new Axis[]
        {
            new Axis
            {
                Name = "Count"
            }
        };
    }
}