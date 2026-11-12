using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
        ScopeFilters = ["All", "Production", "Test"];
        _dependenciesView = CollectionViewSource.GetDefaultView(Dependencies);
        _dependenciesView.Filter = MatchesFilter;
    }

    public ObservableCollection<ArchitectureDependencyContext> Dependencies { get; private set; } = new();

    public ICollectionView DependenciesView => _dependenciesView;

    public IReadOnlyList<string> LayerFilters { get; private set; }

    public IReadOnlyList<string> ScopeFilters { get; }

    public IReadOnlyList<string> IntegrationPatterns { get; } =
        ["RequestResponse", "PublishSubscribe", "Streaming", "SharedStorage", "Other"];

    public IReadOnlyList<string> IntegrationProtocols { get; } =
        ["HTTP", "HTTPS", "gRPC", "AMQP", "Kafka", "WebSocket", "SharedDatabase", "FileTransfer", "Other"];

    public ObservableCollection<ProjectModuleContext> IntegrationModules { get; } = new();
    public ObservableCollection<ArchitectureIntegrationContext> ApplicationIntegrations { get; } = new();
    private ProjectArchitectureContext? _context;

    [ObservableProperty] private ProjectModuleContext? selectedIntegrationSource;
    [ObservableProperty] private ProjectModuleContext? selectedIntegrationTarget;
    [ObservableProperty] private string selectedIntegrationPattern = "RequestResponse";
    [ObservableProperty] private string selectedIntegrationProtocol = "HTTP";
    [ObservableProperty] private string integrationNotes = string.Empty;
    [ObservableProperty] private ArchitectureIntegrationContext? selectedApplicationIntegration;
    [ObservableProperty] private string integrationStatus = string.Empty;

 

    [ObservableProperty] private string projectName = "No assessment loaded";
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedLayer = "All";
    [ObservableProperty] private string selectedScope = "All";
    [ObservableProperty] private ArchitectureDependencyContext? selectedDependency;
    [ObservableProperty] private int totalDependencies;
    [ObservableProperty] private int filteredDependencies;

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedLayerChanged(string value) => RefreshFilter();

    partial void OnSelectedScopeChanged(string value) => RefreshFilter();

    partial void OnSelectedDependencyChanged(ArchitectureDependencyContext? value) =>
        CopySelectedDependencyCommand.NotifyCanExecuteChanged();

    partial void OnSelectedIntegrationSourceChanged(ProjectModuleContext? value) =>
        AddApplicationIntegrationCommand.NotifyCanExecuteChanged();

    partial void OnSelectedIntegrationTargetChanged(ProjectModuleContext? value) =>
        AddApplicationIntegrationCommand.NotifyCanExecuteChanged();

    partial void OnSelectedApplicationIntegrationChanged(ArchitectureIntegrationContext? value) =>
        RemoveApplicationIntegrationCommand.NotifyCanExecuteChanged();


    public void Update(ProjectArchitectureContext? context)
    {
        _context = context;
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

        if (!ScopeFilters.Contains(SelectedScope, StringComparer.OrdinalIgnoreCase))
            SelectedScope = "All";

        OnPropertyChanged(nameof(Dependencies));
        OnPropertyChanged(nameof(DependenciesView));
        OnPropertyChanged(nameof(LayerFilters));

        TotalDependencies = dependencies.Length;

        IntegrationModules.Clear();
        ApplicationIntegrations.Clear();
        if (context is not null)
        {
            foreach (var module in context.ProductionModules.OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase))
                IntegrationModules.Add(module);
            foreach (var integration in context.ApplicationIntegrations)
                ApplicationIntegrations.Add(integration);
        }

        SelectedIntegrationSource = IntegrationModules.FirstOrDefault();
        SelectedIntegrationTarget = IntegrationModules.Skip(1).FirstOrDefault();
        SelectedApplicationIntegration = null;
        IntegrationStatus = string.Empty;
        AddApplicationIntegrationCommand.NotifyCanExecuteChanged();
        RemoveApplicationIntegrationCommand.NotifyCanExecuteChanged();
        RefreshFilter();
    }

    [RelayCommand(CanExecute = nameof(CanAddApplicationIntegration))]
    private void AddApplicationIntegration()
    {
        if (_context is null || SelectedIntegrationSource is not { } source || SelectedIntegrationTarget is not { } target)
            return;

        if (PathsEqual(source.Path, target.Path))
        {
            IntegrationStatus = "Source and target must be different modules.";
            return;
        }

        if (_context.ApplicationIntegrations.Any(integration =>
                PathsEqual(integration.SourceModulePath, source.Path)
                && PathsEqual(integration.TargetModulePath, target.Path)
                && integration.InteractionPattern.Equals(SelectedIntegrationPattern, StringComparison.OrdinalIgnoreCase)
                && integration.Protocol.Equals(SelectedIntegrationProtocol, StringComparison.OrdinalIgnoreCase)))
        {
            IntegrationStatus = "This application integration is already declared.";
            return;
        }

        var integration = new ArchitectureIntegrationContext
        {
            SourceModuleName = source.Name,
            SourceModulePath = Path.GetFullPath(source.Path),
            TargetModuleName = target.Name,
            TargetModulePath = Path.GetFullPath(target.Path),
            InteractionPattern = SelectedIntegrationPattern,
            Protocol = SelectedIntegrationProtocol,
            EvidenceOrigin = "UserDefined",
            Notes = string.IsNullOrWhiteSpace(IntegrationNotes) ? null : IntegrationNotes.Trim()
        };

        _context.ApplicationIntegrations.Add(integration);
        try
        {
            ApplicationIntegrationConfigurationStore.Save(
                _context.RootPath,
                _context.ApplicationIntegrations);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException)
        {
            _context.ApplicationIntegrations.Remove(integration);
            IntegrationStatus = $"Unable to persist the integration declaration: {exception.Message}";
            return;
        }

        ApplicationIntegrations.Add(integration);
        SelectedApplicationIntegration = integration;
        IntegrationNotes = string.Empty;
        IntegrationStatus = "User-defined integration added to the current architecture context.";
    }

    [RelayCommand(CanExecute = nameof(CanRemoveApplicationIntegration))]
    private void RemoveApplicationIntegration()
    {
        if (_context is null || SelectedApplicationIntegration is not { } integration)
            return;

        _context.ApplicationIntegrations.RemoveAll(candidate => candidate.Id == integration.Id);
        try
        {
            ApplicationIntegrationConfigurationStore.Save(
                _context.RootPath,
                _context.ApplicationIntegrations);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException)
        {
            _context.ApplicationIntegrations.Add(integration);
            IntegrationStatus = $"Unable to persist the integration removal: {exception.Message}";
            return;
        }

        ApplicationIntegrations.Remove(integration);
        SelectedApplicationIntegration = null;
        IntegrationStatus = "Integration removed and saved to the repository's .aegis configuration.";
    }

    private bool CanAddApplicationIntegration() =>
        _context is not null && SelectedIntegrationSource is not null
        && SelectedIntegrationTarget is not null && IntegrationModules.Count > 1;

    private bool CanRemoveApplicationIntegration() => SelectedApplicationIntegration is not null;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedLayer = "All";
        SelectedScope = "All";
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
            Scope: {dependency.Scope}
            Source project: {dependency.SourceProjectPath}
            Target project: {dependency.TargetProjectPath}
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

        if (!string.Equals(SelectedScope, "All", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(dependency.Scope, SelectedScope, StringComparison.OrdinalIgnoreCase))
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
            || Contains(dependency.Scope, query)
            || Contains(dependency.SourceProjectPath, query)
            || Contains(dependency.TargetProjectPath, query)
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
