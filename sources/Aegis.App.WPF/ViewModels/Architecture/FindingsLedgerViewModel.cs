using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>Searchable, source-oriented view model for the architecture findings ledger.</summary>
public sealed partial class FindingsLedgerViewModel : ObservableObject
{
    private ICollectionView _findingsView;

    public FindingsLedgerViewModel()
    {
        _findingsView = CollectionViewSource.GetDefaultView(Findings);
        _findingsView.Filter = MatchesFilter;
    }

    public ObservableCollection<RuleDashboardItem> Findings { get; } = new();
    public ICollectionView FindingsView => _findingsView;

    [ObservableProperty] private string projectName = "No assessment loaded";
    [ObservableProperty] private string filterText = string.Empty;
    [ObservableProperty] private int totalFindings;
    [ObservableProperty] private int filteredFindings;
    [ObservableProperty] private int blockerCount;
    [ObservableProperty] private int criticalCount;
    [ObservableProperty] private int highCount;
    [ObservableProperty] private int mediumCount;
    [ObservableProperty] private int lowCount;
    [ObservableProperty] private int infoCount;

    partial void OnFilterTextChanged(string value)
    {
        _findingsView.Refresh();
        FilteredFindings = _findingsView.Cast<object>().Count();
    }

    public void Update(AegisArchitectureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ProjectName = string.IsNullOrWhiteSpace(report.ProjectName) ? "Unknown project" : report.ProjectName;

        var items = report.Results.Select(result => new RuleDashboardItem(
            result.RuleName,
            result.Category,
            result.Severity,
            string.IsNullOrWhiteSpace(result.Target) ? "(project-wide)" : result.Target,
            result.Message,
            result.WeightedImpact)).ToArray();

        Findings.Clear();
        foreach (var item in items)
            Findings.Add(item);

        var counts = report.Results.GroupBy(result => result.Severity)
            .ToDictionary(group => group.Key, group => group.Count());

        TotalFindings = items.Length;
        BlockerCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.Blocker);
        CriticalCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.Critical);
        HighCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.High);
        MediumCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.Medium);
        LowCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.Low);
        InfoCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.Info);
        _findingsView.Refresh();
        FilteredFindings = _findingsView.Cast<object>().Count();
    }

    public void ClearFilter() => FilterText = string.Empty;

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
}
