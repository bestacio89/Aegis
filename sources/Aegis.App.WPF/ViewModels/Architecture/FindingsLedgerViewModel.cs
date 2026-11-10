using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// Searchable, source-oriented view model for investigating architecture findings.
/// The original rule result is retained so evidence and remediation context are not lost.
/// </summary>
public sealed partial class FindingsLedgerViewModel : ObservableObject
{
    private ICollectionView _findingsView;

    public FindingsLedgerViewModel()
    {
        SeverityFilters = ["All", .. Enum.GetNames<ArchitectureRuleSeverity>()];
        _findingsView = CollectionViewSource.GetDefaultView(Findings);
        _findingsView.Filter = MatchesFilter;
    }

    public ObservableCollection<ArchitectureRuleresult> Findings { get; private set; } = new();

    public ICollectionView FindingsView => _findingsView;

    public IReadOnlyList<string> SeverityFilters { get; }

    [ObservableProperty] private string projectName = "No assessment loaded";
    [ObservableProperty] private string filterText = string.Empty;
    [ObservableProperty] private string selectedSeverity = "All";
    [ObservableProperty] private ArchitectureRuleresult? selectedFinding;
    [ObservableProperty] private int totalFindings;
    [ObservableProperty] private int filteredFindings;
    [ObservableProperty] private int blockerCount;
    [ObservableProperty] private int criticalCount;
    [ObservableProperty] private int highCount;
    [ObservableProperty] private int mediumCount;
    [ObservableProperty] private int lowCount;
    [ObservableProperty] private int infoCount;

    partial void OnFilterTextChanged(string value) => RefreshFilter();

    partial void OnSelectedSeverityChanged(string value) => RefreshFilter();

    partial void OnSelectedFindingChanged(ArchitectureRuleresult? value) =>
        CopySelectedFindingCommand.NotifyCanExecuteChanged();

    public void Update(AegisArchitectureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        ProjectName = string.IsNullOrWhiteSpace(report.ProjectName)
            ? "Unknown project"
            : report.ProjectName;

        // Keep the original result objects: the ledger must expose the evaluator's
        // evidence, source location, code snippet, recommendation, and provenance.
        var items = report.Results
            .Where(result => !result.IsCompliant)
            .OrderByDescending(result => result.Severity)
            .ThenBy(result => result.RuleName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SelectedFinding = null;
        Findings = new ObservableCollection<ArchitectureRuleresult>(items);
        _findingsView = CollectionViewSource.GetDefaultView(Findings);
        _findingsView.Filter = MatchesFilter;

        OnPropertyChanged(nameof(Findings));
        OnPropertyChanged(nameof(FindingsView));

        var counts = items
            .GroupBy(result => result.Severity)
            .ToDictionary(group => group.Key, group => group.Count());

        TotalFindings = items.Length;
        BlockerCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.Blocker);
        CriticalCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.Critical);
        HighCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.High);
        MediumCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.Medium);
        LowCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.Low);
        InfoCount = counts.GetValueOrDefault(ArchitectureRuleSeverity.Info);

        RefreshFilter();
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
        SelectedSeverity = "All";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFinding))]
    private void CopySelectedFinding()
    {
        if (SelectedFinding is not { } finding)
            return;

        var sourceLocation = string.IsNullOrWhiteSpace(finding.FilePath)
            ? finding.Target
            : finding.LineNumber is { } line
                ? $"{finding.FilePath}:{line}"
                : finding.FilePath;

        var content = $"""
            {finding.RuleName} [{finding.Severity}]
            Category: {finding.Category}
            Domain: {finding.Domain}
            Target: {finding.Target}
            Source: {sourceLocation}

            {finding.Message}

            Recommendation:
            {finding.Recommendation ?? "No recommendation supplied."}

            Evidence:
            {string.Join(Environment.NewLine, finding.Evidence)}
            """;

        Clipboard.SetText(content);
    }

    private bool HasSelectedFinding() => SelectedFinding is not null;

    private void RefreshFilter()
    {
        _findingsView.Refresh();

        if (SelectedFinding is not null
            && !_findingsView.Contains(SelectedFinding))
        {
            SelectedFinding = null;
        }

        FilteredFindings = _findingsView.Cast<object>().Count();
    }

    private bool MatchesFilter(object item)
    {
        if (item is not ArchitectureRuleresult finding)
            return false;

        if (!string.Equals(SelectedSeverity, "All", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(finding.Severity.ToString(), SelectedSeverity, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        var query = FilterText.Trim();
        return Contains(finding.RuleId, query)
            || Contains(finding.RuleName, query)
            || Contains(finding.Category.ToString(), query)
            || Contains(finding.Severity.ToString(), query)
            || Contains(finding.Domain, query)
            || Contains(finding.Target, query)
            || Contains(finding.FilePath, query)
            || Contains(finding.Namespace, query)
            || Contains(finding.Message, query)
            || Contains(finding.Recommendation, query)
            || finding.Evidence.Any(evidence => Contains(evidence, query));
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}