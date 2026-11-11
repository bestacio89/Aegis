using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// Presents every detector-reported dependency as an independently filterable
/// relationship record. Records are not aggregated or inferred.
/// </summary>
public sealed partial class RelationshipLedgerViewModel : ObservableObject
{
    private ICollectionView _dependenciesView;

    public RelationshipLedgerViewModel()
    {
        LayerFilters = ["All"];
        _dependenciesView = CollectionViewSource.GetDefaultView(Dependencies);
        _dependenciesView.Filter = MatchesFilter;
    }

    public ObservableCollection<ArchitectureDependencyContext> Dependencies { get; private set; } = new();

    public ICollectionView DependenciesView => _dependenciesView;

    public IReadOnlyList<string> LayerFilters { get; private set; }

    [ObservableProperty] private string projectName = "No assessment loaded";
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedLayer = "All";
    [ObservableProperty] private ArchitectureDependencyContext? selectedDependency;
    [ObservableProperty] private int totalDependencies;
    [ObservableProperty] private int filteredDependencies;

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedLayerChanged(string value) => RefreshFilter();

    partial void OnSelectedDependencyChanged(ArchitectureDependencyContext? value) =>
        CopySelectedDependencyCommand.NotifyCanExecuteChanged();

    public void Update(ProjectArchitectureContext? context)
    {
        ProjectName = string.IsNullOrWhiteSpace(context?.ProjectName)
            ? "No architecture context available"
            : context.ProjectName;

        var dependencies = context?.Dependencies
            .Where(dependency =>
                !string.IsNullOrWhiteSpace(dependency.Source)
                && !string.IsNullOrWhiteSpace(dependency.Target))
            .OrderBy(dependency => dependency.SourceLayer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(dependency => dependency.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(dependency => dependency.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(dependency => dependency.DependencyType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(dependency => dependency.File, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        SelectedDependency = null;
        Dependencies = new ObservableCollection<ArchitectureDependencyContext>(dependencies);
        _dependenciesView = CollectionViewSource.GetDefaultView(Dependencies);
        _dependenciesView.Filter = MatchesFilter;

        LayerFilters = ["All", .. dependencies
            .SelectMany(dependency => new[] { dependency.SourceLayer, dependency.TargetLayer })
            .Where(layer => !string.IsNullOrWhiteSpace(layer))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(layer => layer, StringComparer.OrdinalIgnoreCase)];

        if (!LayerFilters.Contains(SelectedLayer, StringComparer.OrdinalIgnoreCase))
            SelectedLayer = "All";

        OnPropertyChanged(nameof(Dependencies));
        OnPropertyChanged(nameof(DependenciesView));
        OnPropertyChanged(nameof(LayerFilters));

        TotalDependencies = dependencies.Length;
        RefreshFilter();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedLayer = "All";
        SelectedDependency = null;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedDependency))]
    private void CopySelectedDependency()
    {
        if (SelectedDependency is not { } dependency)
            return;

        Clipboard.SetText($"""
            {dependency.Source} -> {dependency.Target}
            Source layer: {dependency.SourceLayer}
            Target layer: {dependency.TargetLayer}
            Dependency type: {dependency.DependencyType}
            Evidence file: {dependency.File}
            """);
    }

    private bool HasSelectedDependency() => SelectedDependency is not null;

    private bool MatchesFilter(object item)
    {
        if (item is not ArchitectureDependencyContext dependency)
            return false;

        if (!string.Equals(SelectedLayer, "All", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(dependency.SourceLayer, SelectedLayer, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(dependency.TargetLayer, SelectedLayer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var query = SearchText.Trim();
        return Contains(dependency.Source, query)
            || Contains(dependency.Target, query)
            || Contains(dependency.SourceLayer, query)
            || Contains(dependency.TargetLayer, query)
            || Contains(dependency.DependencyType, query)
            || Contains(dependency.File, query);
    }

    private void RefreshFilter()
    {
        _dependenciesView.Refresh();
        if (SelectedDependency is not null && !_dependenciesView.Contains(SelectedDependency))
            SelectedDependency = null;

        FilteredDependencies = _dependenciesView.Cast<object>().Count();
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
