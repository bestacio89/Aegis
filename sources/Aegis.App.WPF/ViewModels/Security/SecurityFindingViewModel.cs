using Aegis.Shared.Security.Enums;
using Aegis.Wpf.Models.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace Aegis.Wpf.ViewModels.Security;

public sealed record SecurityDomainOption(SecurityCategory? Value, string Label);

public sealed partial class SecurityFindingsViewModel : ObservableObject
{
    private static readonly SecurityDomainOption AllDomains = new(null, "All domains");

    private readonly ObservableCollection<SecurityFindingItem> _items = new();

    public SecurityFindingsViewModel()
    {
        FilteredFindings = CollectionViewSource.GetDefaultView(_items);
        FilteredFindings.Filter = Matches;
        FilteredFindings.SortDescriptions.Add(
            new SortDescription(nameof(SecurityFindingItem.Severity), ListSortDirection.Descending));

        DomainOptions = new ObservableCollection<SecurityDomainOption> { AllDomains };
    }

    public ICollectionView FilteredFindings { get; }

    public ObservableCollection<SecurityDomainOption> DomainOptions { get; }

    public IReadOnlyList<SecuritySeverity> SeverityOptions { get; } = Enum.GetValues<SecuritySeverity>();

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private SecuritySeverity minimumSeverity = SecuritySeverity.Low;
    [ObservableProperty] private SecurityDomainOption selectedDomainOption = AllDomains;
    [ObservableProperty] private SecurityFindingItem? selectedFinding;
    [ObservableProperty] private string countLabel = "0 findings";

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnMinimumSeverityChanged(SecuritySeverity value) => Refresh();
    partial void OnSelectedDomainOptionChanged(SecurityDomainOption value) => Refresh();

    public void Update(SecurityScanResult result)
    {
        _items.Clear();
        foreach (var f in result.Findings)
            _items.Add(f);

        DomainOptions.Clear();
        DomainOptions.Add(AllDomains);
        foreach (var d in result.Domains)
            DomainOptions.Add(new SecurityDomainOption(d.Domain, d.Domain.ToString()));

        SearchText = string.Empty;
        MinimumSeverity = SecuritySeverity.Low;
        SelectedDomainOption = AllDomains;
        SelectedFinding = null;

        Refresh();
    }

    /// <summary>Drill-down from the Domains tab: show everything recorded for one domain.</summary>
    public void ShowDomain(SecurityCategory domain)
    {
        MinimumSeverity = SecuritySeverity.Info;
        SelectedDomainOption = DomainOptions.FirstOrDefault(o => o.Value == domain) ?? AllDomains;
        Refresh();
    }

    private bool Matches(object item)
    {
        if (item is not SecurityFindingItem f)
            return false;

        if (f.Severity < MinimumSeverity)
            return false;

        var domain = SelectedDomainOption?.Value;
        if (domain.HasValue && f.Domain != domain.Value)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var q = SearchText.Trim();
        return Contains(f.RuleId, q)
            || Contains(f.Title, q)
            || Contains(f.Target, q)
            || Contains(f.Location, q)
            || Contains(f.Detail, q);
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private void Refresh()
    {
        FilteredFindings.Refresh();
        var shown = FilteredFindings.Cast<object>().Count();
        CountLabel = $"{shown} of {_items.Count} findings";
    }
}