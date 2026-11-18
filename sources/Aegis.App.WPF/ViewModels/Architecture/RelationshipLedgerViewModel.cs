using Aegis.Architecture.Analysis;
using Aegis.Architecture.Analysis.Projection;
using Aegis.Shared.Architecture.Enums;
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
/// Presents the authoritative unified graph projection as a filterable relationship
/// ledger. Unprojected observations are inspected in their dedicated workspace.
/// </summary>
public sealed partial class RelationshipLedgerViewModel : ObservableObject
{
    private ICollectionView _dependenciesView;
    private readonly ArchitectureGraphViewOptions _graphOptions;

    public RelationshipLedgerViewModel(ArchitectureGraphViewOptions? graphOptions = null)
    {
        _graphOptions = graphOptions ?? new ArchitectureGraphViewOptions();
        _graphOptions.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ArchitectureGraphViewOptions.Depth))
            {
                OnPropertyChanged(nameof(RollupDepth));
                Update(_context);
            }
        };
        LayerFilters = ["All"];
        ScopeFilters = ["All", "Production", "Test"];
        _dependenciesView = CollectionViewSource.GetDefaultView(Dependencies);
        _dependenciesView.Filter = MatchesFilter;
    }

    public ObservableCollection<ArchitectureRelationshipRecordViewModel> Dependencies { get; private set; } = new();

    public ICollectionView DependenciesView => _dependenciesView;

    public IReadOnlyList<string> LayerFilters { get; private set; }
    public int RollupDepth { get => _graphOptions.Depth; set => _graphOptions.Depth = value; }
    public string BasePackageLabel { get; private set; } = "Base package: not available";

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
    [ObservableProperty] private ArchitectureRelationshipRecordViewModel? selectedDependency;
    [ObservableProperty] private int totalDependencies;
    [ObservableProperty] private int filteredDependencies;

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedLayerChanged(string value) => RefreshFilter();

    partial void OnSelectedScopeChanged(string value) => RefreshFilter();

    partial void OnSelectedDependencyChanged(ArchitectureRelationshipRecordViewModel? value) =>
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

        var projection = context?.ArchitectureGraphProjection ?? new ArchitectureGraphProjection([], [], []);
        var rollup = ArchitectureGraphRollup.Apply(projection, _context is null ? null : ArchitecturePackageBaseResolver.Resolve(_context), _graphOptions.Depth);
        BasePackageLabel = string.IsNullOrWhiteSpace(rollup.BasePackage) ? "Base package: package root" : $"Base package: {rollup.BasePackage}";
        OnPropertyChanged(nameof(BasePackageLabel));
        var nodesById = rollup.Nodes.ToDictionary(node => node.GroupKey, node => new ArchitectureGraphNode
        {
            Id = node.GroupKey, Name = node.Name, Kind = node.Kind, Language = node.Language,
            ComponentKind = node.ComponentKind, ArchitecturalRole = node.ArchitecturalRole, ModulePath = node.ModulePath,
            ResourceKind = node.ResourceKind, ResourceProvider = node.ResourceProvider, ResourceIdentity = node.ResourceIdentity,
            IsTestScoped = node.IsTestScoped
        }, StringComparer.Ordinal);
        var dependencies = rollup.Edges
            .Select(edge => CreateRelationshipRecord(edge, nodesById))
            .Where(record => record is not null).Cast<ArchitectureRelationshipRecordViewModel>()
            .OrderBy(record => record.GraphDomain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.GraphEdgeId, StringComparer.Ordinal).ToArray();

        SelectedDependency = null;
        Dependencies = new ObservableCollection<ArchitectureRelationshipRecordViewModel>(dependencies);
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

    private static ArchitectureRelationshipRecordViewModel? CreateRelationshipRecord(
        ArchitectureGraphRollupEdge edge,
        IReadOnlyDictionary<string, ArchitectureGraphNode> nodesById)
    {
        if (!nodesById.TryGetValue(edge.SourceGroupKey, out var source) || !nodesById.TryGetValue(edge.TargetGroupKey, out var target))
            return null;
        var kinds = edge.RelationshipKinds.Select(kind => kind.ToString()).Distinct(StringComparer.Ordinal).OrderBy(kind => kind, StringComparer.Ordinal).ToArray();
        var classified = kinds.Where(kind => !kind.Equals(nameof(ArchitectureRelationshipKind.Unknown), StringComparison.Ordinal)).ToArray();
        var providerKind = string.Join(", ", edge.ProviderRelationshipKinds);
        var type = classified.Length > 0 ? string.Join(", ", classified)
            : !string.IsNullOrWhiteSpace(providerKind) ? providerKind : kinds.Length > 0 ? string.Join(", ", kinds) : "Unclassified";
        return new ArchitectureRelationshipRecordViewModel
        {
            GraphEdgeId = $"{edge.SourceGroupKey}->{edge.TargetGroupKey}:{edge.Domain}:{edge.Scope}",
            GraphDomain = edge.Domain.ToString(),
            RelationshipLevel = edge.Domain switch
            {
                ArchitectureGraphEdgeDomain.Build => "Build topology",
                ArchitectureGraphEdgeDomain.NativeSemantic => "Native semantics",
                ArchitectureGraphEdgeDomain.Messaging => "Messaging",
                ArchitectureGraphEdgeDomain.Rpc => "RPC",
                ArchitectureGraphEdgeDomain.DataAccess => "Data access",
                ArchitectureGraphEdgeDomain.Api => "HTTP / API",
                _ => "Unknown graph domain"
            },
            Source = source.Name, SourceLayer = GetNodeClassification(source), SourceProjectPath = source.ModulePath ?? string.Empty,
            Target = target.Name, TargetLayer = GetNodeClassification(target), TargetProjectPath = target.ModulePath ?? string.Empty,
            DependencyType = type, ProviderRelationshipKind = providerKind, Direction = $"{source.Name} → {target.Name}",
            Scope = edge.Scope, RelationshipCount = edge.RelationshipCount, GroupDepth = edge.GroupDepth,
            EvidenceOccurrenceCount = edge.Evidence.Count,
            IsInternal = string.Equals(edge.SourceGroupKey, edge.TargetGroupKey, StringComparison.Ordinal),
            EvidenceReferences = FormatGraphEvidenceReferences(edge.Evidence), ContributingLeafEdgeIds = edge.LeafEdgeIds,
            EvidenceFiles = edge.EvidenceFiles
        };
    }

    private static string GetNodeClassification(ArchitectureGraphNode node) =>
        node.ResourceKind
        ?? node.ArchitecturalRole
        ?? node.ComponentKind
        ?? node.Kind.ToString();

    private static string FormatGraphEvidenceReferences(
        IEnumerable<ArchitectureGraphEvidenceReference> references) =>
        string.Join(
            Environment.NewLine,
            references
                .OrderBy(reference => reference.Domain)
                .ThenBy(reference => reference.OccurrenceIndex)
                .Select(reference =>
                    $"{reference.Domain}[{reference.OccurrenceIndex}]"
                    + (string.IsNullOrWhiteSpace(reference.EvidenceId) ? string.Empty : $":{reference.EvidenceId}")
                    + (string.IsNullOrWhiteSpace(reference.ProviderId) ? string.Empty : $" ({reference.ProviderId})")));

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
            Relationship level: {dependency.RelationshipLevel}
            Graph domain: {dependency.GraphDomain}
            Graph edge: {dependency.GraphEdgeId}
            Direction: {dependency.Direction}
            {dependency.Source} -> {dependency.Target}
            Evidence occurrences: {dependency.EvidenceOccurrenceCount}
            Source classification: {dependency.SourceLayer}
            Target classification: {dependency.TargetLayer}
            Dependency type: {dependency.DependencyType}
            Provider relationship kind: {dependency.ProviderRelationshipKind}
            Scope: {dependency.Scope}
            Source module: {dependency.SourceProjectPath}
            Target module: {dependency.TargetProjectPath}
            Internal dependency: {dependency.IsInternal}
            Evidence references: {dependency.EvidenceReferences}
            """);
    }

    private bool HasSelectedDependency() => SelectedDependency is not null;

    private bool MatchesFilter(object item)
    {
        if (item is not ArchitectureRelationshipRecordViewModel dependency)
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
        return Contains(dependency.RelationshipLevel, query)
            || Contains(dependency.GraphDomain, query)
            || Contains(dependency.GraphEdgeId, query)
            || Contains(dependency.Direction, query)
            || Contains(dependency.ProviderRelationshipKind, query)
            || Contains(dependency.Source, query)
            || Contains(dependency.Target, query)
            || Contains(dependency.SourceLayer, query)
            || Contains(dependency.TargetLayer, query)
            || Contains(dependency.DependencyType, query)
            || Contains(dependency.Scope, query)
            || Contains(dependency.SourceProjectPath, query)
            || Contains(dependency.TargetProjectPath, query)
            || Contains(dependency.EvidenceReferences, query);
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
