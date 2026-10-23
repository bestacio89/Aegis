using Aegis.Wpf.models;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;

namespace Aegis.Wpf.ViewModels.Architecture;

public sealed partial class RuleDashboardViewModel : ObservableObject
{
    private readonly ILogger<RuleDashboardViewModel> _logger;
    private ObservableCollection<RuleDashboardItem> _ruleResults = new();
    private ICollectionView _ruleResultsView;

    public RuleDashboardViewModel(ILogger<RuleDashboardViewModel> logger)
    {
        _logger = logger;
        _ruleResultsView = CollectionViewSource.GetDefaultView(_ruleResults);
        _ruleResultsView.Filter = MatchesFilter;
    }

    public ObservableCollection<RuleDashboardItem> RuleResults
    {
        get => _ruleResults;
        private set
        {
            if (!SetProperty(ref _ruleResults, value))
                return;

            _ruleResultsView = CollectionViewSource.GetDefaultView(value);
            _ruleResultsView.Filter = MatchesFilter;
            OnPropertyChanged(nameof(RuleResultsView));
        }
    }

    public ICollectionView RuleResultsView => _ruleResultsView;

    private string _filterText = string.Empty;
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetProperty(ref _filterText, value ?? string.Empty))
                return;

            _ruleResultsView.Refresh();
        }
    }

    [ObservableProperty] private int totalViolations;
    [ObservableProperty] private int blockerCount;
    [ObservableProperty] private int criticalCount;
    [ObservableProperty] private int highCount;
    [ObservableProperty] private int mediumCount;
    [ObservableProperty] private string registeredRules = "n/a";
    [ObservableProperty] private string applicableRules = "n/a";
    [ObservableProperty] private string evaluatedRules = "n/a";
    [ObservableProperty] private string notEvaluatedRules = "n/a";
    [ObservableProperty] private string notApplicableRules = "n/a";
    [ObservableProperty] private string unsupportedRules = "n/a";
    [ObservableProperty] private string rulesWithErrors = "n/a";
    [ObservableProperty] private string metricContractFailures = "n/a";
    [ObservableProperty] private string ruleAccountingDetails = "Run an assessment to populate rule accounting.";
    [ObservableProperty] private string mostAffectedCategory = "-";
    [ObservableProperty] private double projectHealth;
    [ObservableProperty] private double weightedCompliance;
    [ObservableProperty] private ISeries[] severitySeries = [];
    [ObservableProperty] private ISeries[] categorySeries = [];
    [ObservableProperty] private Axis[] categoryAxes = [];
    [ObservableProperty] private Axis[] valueAxes = [];

    public void Update(AegisArchitectureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        try
        {
            // Prepare presentation rows before publishing one collection replacement.
            // This avoids one UI notification per finding during large report refreshes.
            var items = report.Results
                .Select(result => new RuleDashboardItem(
                    result.RuleName,
                    result.Category,
                    result.Severity,
                    Path.GetFileName(result.Target),
                    result.Message,
                    result.WeightedImpact))
                .ToArray();

            var results = report.Results;
            var severityCounts = results
                .GroupBy(result => result.Severity)
                .ToDictionary(group => group.Key, group => group.Count());

            RuleResults = new ObservableCollection<RuleDashboardItem>(items);

            TotalViolations = report.TotalViolations;

            var stats = report.EvaluationStats;
            RegisteredRules = stats is null ? "n/a" : stats.RulesRegistered.ToString("N0");
            ApplicableRules = stats is null ? "n/a" : (stats.RulesApplicable + stats.RulesUnsupported).ToString("N0");
            EvaluatedRules = stats is null ? "n/a" : stats.RulesEvaluated.ToString("N0");
            NotEvaluatedRules = stats is null ? "n/a" : stats.RulesNotEvaluated.Count.ToString("N0");
            NotApplicableRules = stats is null ? "n/a" : stats.RulesNotApplicable.Count.ToString("N0");
            UnsupportedRules = stats is null ? "n/a" : stats.RulesUnsupported.ToString("N0");
            RulesWithErrors = stats is null ? "n/a" : stats.RulesWithErrors.ToString("N0");
            MetricContractFailures = stats is null ? "n/a" : stats.MetricContractFailures.ToString("N0");
            RuleAccountingDetails = BuildRuleAccountingDetails(stats);

            BlockerCount = severityCounts.GetValueOrDefault(ArchitectureRuleSeverity.Blocker);
            CriticalCount = severityCounts.GetValueOrDefault(ArchitectureRuleSeverity.Critical);
            HighCount = severityCounts.GetValueOrDefault(ArchitectureRuleSeverity.High);
            MediumCount = severityCounts.GetValueOrDefault(ArchitectureRuleSeverity.Medium);

            MostAffectedCategory = results
                .GroupBy(result => result.Category.ToString())
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Key)
                .FirstOrDefault() ?? "-";

            ProjectHealth = report.Metrics.ProjectHealthIndex;
            // The report stores weighted compliance as a ratio; display it as a percentage.
            WeightedCompliance = report.Metrics.WeightedCompliance * 100d;

            BuildSeverityChart(severityCounts);
            BuildCategoryChart(report);

            _logger.LogInformation(
                "Rule dashboard updated from report. {FindingCount} findings.",
                items.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed updating rule dashboard.");
            throw;
        }
    }

    private bool MatchesFilter(object item)
    {
        if (item is not RuleDashboardItem finding || string.IsNullOrWhiteSpace(FilterText))
            return true;

        var query = FilterText.Trim();
        return Contains(finding.RuleName, query)
            || Contains(finding.Category.ToString(), query)
            || Contains(finding.Severity.ToString(), query)
            || Contains(finding.Target, query)
            || Contains(finding.Message, query);
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static string BuildRuleAccountingDetails(RuleEvaluationStats? stats)
    {
        if (stats is null)
            return "Rule accounting evidence is unavailable for this report.";

        var details = new List<string>();
        details.AddRange(stats.UnsupportedRuleReasons
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .Select(pair => $"Unsupported · {pair.Key}: {pair.Value}"));
        details.AddRange(stats.NotEvaluatedReasons
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, 8 - details.Count))
            .Select(pair => $"Not evaluated · {pair.Key}: {pair.Value}"));

        if (details.Count == 0)
            return stats.RulesNotApplicable.Count > 0
                ? $"No unsupported or unevaluated rules were recorded. {stats.RulesNotApplicable.Count:N0} rule(s) were classified as not applicable."
                : "All recorded applicable rules have evaluation evidence.";

        var remaining = stats.UnsupportedRuleReasons.Count + stats.NotEvaluatedReasons.Count - details.Count;
        if (remaining > 0)
            details.Add($"…and {remaining:N0} additional rule status detail(s).");

        return string.Join(Environment.NewLine, details);
    }

    private void BuildSeverityChart(IReadOnlyDictionary<ArchitectureRuleSeverity, int> counts)
    {
        SeveritySeries = Enum.GetValues<ArchitectureRuleSeverity>()
            .Select(severity => new { Severity = severity, Count = counts.GetValueOrDefault(severity) })
            .Where(item => item.Count > 0)
            .Select(item => (ISeries)new PieSeries<int>
            {
                Values = [item.Count],
                Name = item.Severity.ToString(),
                Fill = new SolidColorPaint(GetSeverityColor(item.Severity))
            })
            .ToArray();
    }

    private void BuildCategoryChart(AegisArchitectureReport report)
    {
        // Compliance scores include clean categories, not just categories with findings.
        var categories = report.ComplianceScores
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .ToArray();

        CategorySeries = categories.Length == 0
            ? []
            : [
                new ColumnSeries<double>
                {
                    Values = categories.Select(pair => pair.Value).ToArray(),
                    Name = "Compliance %",
                    Fill = new SolidColorPaint(new SKColor(79, 168, 216))
                }
            ];

        CategoryAxes = categories.Length == 0
            ? []
            : [
                new Axis
                {
                    Labels = categories.Select(pair => pair.Key.ToString()).ToArray(),
                    LabelsRotation = categories.Length > 7 ? 35 : 0
                }
            ];

        ValueAxes =
        [
            new Axis
            {
                Name = "Compliance %",
                MinLimit = 0,
                MaxLimit = 100
            }
        ];
    }

    private static SKColor GetSeverityColor(ArchitectureRuleSeverity severity) =>
        severity switch
        {
            ArchitectureRuleSeverity.Blocker => new SKColor(255, 59, 92),
            ArchitectureRuleSeverity.Critical => new SKColor(229, 72, 77),
            ArchitectureRuleSeverity.High => new SKColor(232, 162, 61),
            ArchitectureRuleSeverity.Medium => new SKColor(235, 197, 75),
            ArchitectureRuleSeverity.Low => new SKColor(61, 220, 139),
            ArchitectureRuleSeverity.Info => new SKColor(79, 168, 216),
            _ => new SKColor(137, 147, 166)
        };
}
