using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// Presents a navigable graph projection of detector-reported dependencies.
/// It does not infer, validate, or mutate architectural relationships.
/// </summary>
public sealed partial class DependencyGraphViewModel : ObservableObject
{
    private const double GraphLeftMargin = 32;
    private const double GraphTopMargin = 88;
    private const double GraphColumnSpacing = 292;
    private const double GraphRowSpacing = 104;

    private ProjectArchitectureContext? _context;
    private IReadOnlyList<GraphRelation> _activeRelations = [];
    private IReadOnlyList<GraphNode> _activeNodes = [];
    private bool _refreshingProjection;
    private bool _hasUserSelectedProjection;

    public DependencyGraphViewModel()
    {
        LayerFilters = ["All"];
        GraphProjectionOptions = ["Build modules"];
    }

    public ObservableCollection<ArchitectureDependencyContext> Dependencies { get; private set; } = new();

    public IReadOnlyList<string> GraphProjectionOptions { get; private set; }

    public string GroupingLabel => GraphProjection switch
    {
        "Architectural components" => "ARCHITECTURAL ROLES",
        "Code containers" => "CONTAINER KINDS",
        _ => "BUILD MODULE TYPES"
    };

    public string NodeCountLabel => GraphProjection switch
    {
        "Architectural components" => "ARCHITECTURAL COMPONENTS",
        "Code containers" => "CODE CONTAINERS",
        _ => "BUILD MODULES"
    };

    public string ProjectionDescription => GraphProjection switch
    {
        "Architectural components" =>
            "Logical architecture inferred or declared from code-container membership. Select a component to inspect its incoming and outgoing dependencies.",
        "Code containers" =>
            "Language-native packages, namespaces, and modules. This projection preserves the semantic boundaries discovered by each language provider.",
        _ =>
            "Independently discovered build-system modules and project references. This projection describes build boundaries, not logical architecture."
    };

    public ObservableCollection<ArchitectureGraphNodeViewModel> GraphNodes { get; } = new();

    public ObservableCollection<ArchitectureGraphEdgeViewModel> GraphEdges { get; } = new();

    public ObservableCollection<ArchitectureGraphLayerHeaderViewModel> GraphLayerHeaders { get; } = new();

    public IReadOnlyList<string> LayerFilters { get; private set; }

    [ObservableProperty] private string projectName = "No assessment loaded";
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string graphProjection = "Build modules";
    [ObservableProperty] private string selectedLayer = "All";
    [ObservableProperty] private ArchitectureGraphNodeViewModel? selectedGraphNode;
    [ObservableProperty] private int totalDependencies;
    [ObservableProperty] private int testRelationshipCount;
    [ObservableProperty] private int filteredDependencies;
    [ObservableProperty] private int componentCount;
    [ObservableProperty] private int layerCount;
    [ObservableProperty] private double graphWidth = 960;
    [ObservableProperty] private double graphHeight = 520;

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnGraphProjectionChanged(string value)
    {
        OnPropertyChanged(nameof(GroupingLabel));
        if (!_refreshingProjection)
        {
            _hasUserSelectedProjection = true;
            RefreshProjection();
        }
    }

    partial void OnSelectedLayerChanged(string value) => RefreshFilter();

    partial void OnSelectedGraphNodeChanged(ArchitectureGraphNodeViewModel? value)
    {
        foreach (var node in GraphNodes)
            node.IsSelected = ReferenceEquals(node, value);

    }

    public void Update(ProjectArchitectureContext? context)
    {
        _context = context;
        ProjectName = string.IsNullOrWhiteSpace(context?.ProjectName)
            ? "No architecture context available"
            : context.ProjectName;

        var dependencies = context?.ProductionDependencies
            .Where(dependency =>
                !string.IsNullOrWhiteSpace(dependency.Source)
                && !string.IsNullOrWhiteSpace(dependency.Target))
            .OrderBy(dependency => dependency.SourceLayer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(dependency => dependency.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(dependency => dependency.Target, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        SelectedGraphNode = null;
        _hasUserSelectedProjection = false;
        Dependencies = new ObservableCollection<ArchitectureDependencyContext>(dependencies);
        OnPropertyChanged(nameof(Dependencies));

        RefreshProjection();
    }

    private void RefreshProjection()
    {
        if (_refreshingProjection)
            return;

        _refreshingProjection = true;
        try
        {
            var buildDependencies = _context?.ProductionDependencies
                .Where(dependency =>
                    !string.IsNullOrWhiteSpace(dependency.Source)
                    && !string.IsNullOrWhiteSpace(dependency.Target))
                .ToArray() ?? [];

            var codeContainerDependencies = (_context?.ProductionComponentDependencies ?? [])
                .Concat((_context?.ArchitectureComponentInternalDependencies ?? [])
                    .Where(dependency => string.Equals(dependency.Scope, "Production", StringComparison.OrdinalIgnoreCase)))
                .Where(dependency =>
                    !string.IsNullOrWhiteSpace(dependency.SourceComponentId)
                    && !string.IsNullOrWhiteSpace(dependency.TargetComponentId))
                .ToArray();

            var architecturalDependencies = _context?.ProductionArchitecturalComponentDependencies
                .Where(dependency =>
                    !string.IsNullOrWhiteSpace(dependency.SourceComponentId)
                    && !string.IsNullOrWhiteSpace(dependency.TargetComponentId))
                .ToArray() ?? [];

            var codeContainers = _context?.ProductionComponents ?? [];
            var architecturalComponents = _context?.ArchitecturalComponents
                .Where(component => component.ProductionSymbolCount > 0)
                .ToArray() ?? [];

            var projectionOptions = new List<string> { "Build modules" };
            if (codeContainers.Count > 0)
                projectionOptions.Add("Code containers");
            if (architecturalComponents.Length > 0)
                projectionOptions.Add("Architectural components");
            GraphProjectionOptions = projectionOptions;

            // A single-module repository can still have meaningful code-container
            // and logical architecture graphs. Prefer the most informative
            // available semantic projection without conflating its identity model.
            var preferredProjection = architecturalComponents.Length > 0
                ? "Architectural components"
                : codeContainers.Count > 0
                    ? "Code containers"
                    : "Build modules";

            if (!GraphProjectionOptions.Contains(GraphProjection, StringComparer.OrdinalIgnoreCase)
                || (!_hasUserSelectedProjection
                    && !string.Equals(GraphProjection, preferredProjection, StringComparison.OrdinalIgnoreCase)))
            {
                GraphProjection = preferredProjection;
            }

            _activeNodes = GraphProjection switch
            {
                "Architectural components" => architecturalComponents
                    .Select(component => new GraphNode(
                        component.Id,
                        component.Name,
                        string.IsNullOrWhiteSpace(component.Role) ? "Unclassified" : component.Role))
                    .ToArray(),
                "Code containers" => codeContainers
                    .Select(component => new GraphNode(
                        component.Id,
                        component.QualifiedName,
                        component.ComponentKind))
                    .ToArray(),
                _ => []
            };

            _activeRelations = GraphProjection switch
            {
                "Architectural components" => architecturalDependencies.Select(dependency => new GraphRelation(
                    dependency.SourceComponentId,
                    dependency.TargetComponentId,
                    dependency.SourceComponentName,
                    dependency.TargetComponentName,
                    _activeNodes.FirstOrDefault(node => node.Id == dependency.SourceComponentId)?.Group ?? "Unclassified",
                    _activeNodes.FirstOrDefault(node => node.Id == dependency.TargetComponentId)?.Group ?? "Unclassified",
                    dependency.RelationshipKinds.Count > 0
                        ? string.Join(", ", dependency.RelationshipKinds.Select(kind => kind.ToString()).Distinct(StringComparer.OrdinalIgnoreCase))
                        : "Semantic dependency",
                    string.Join(", ", dependency.EvidenceFiles),
                    dependency.RelationshipCount)).ToArray(),
                "Code containers" => codeContainerDependencies.Select(dependency => new GraphRelation(
                    dependency.SourceComponentId,
                    dependency.TargetComponentId,
                    dependency.SourceComponentName,
                    dependency.TargetComponentName,
                    _context?.ArchitectureComponents.FirstOrDefault(component => component.Id == dependency.SourceComponentId)?.ComponentKind ?? "Unclassified",
                    _context?.ArchitectureComponents.FirstOrDefault(component => component.Id == dependency.TargetComponentId)?.ComponentKind ?? "Unclassified",
                    dependency.DependencyType,
                    string.Join(", ", dependency.EvidenceFiles),
                    dependency.RelationshipCount)).ToArray(),
                _ => buildDependencies.Select(dependency => new GraphRelation(
                    string.IsNullOrWhiteSpace(dependency.SourceProjectPath) ? dependency.Source : dependency.SourceProjectPath,
                    string.IsNullOrWhiteSpace(dependency.TargetProjectPath) ? dependency.Target : dependency.TargetProjectPath,
                    dependency.Source,
                    dependency.Target,
                    dependency.SourceLayer,
                    dependency.TargetLayer,
                    dependency.DependencyType,
                    dependency.File,
                    1)).ToArray()
            };

            TestRelationshipCount = GraphProjection switch
            {
                "Architectural components" => _context?.TestArchitecturalComponentDependencies.Sum(dependency => dependency.RelationshipCount) ?? 0,
                "Code containers" => (_context?.TestComponentDependencies.Sum(dependency => dependency.RelationshipCount) ?? 0)
                    + (_context?.ArchitectureComponentInternalDependencies
                        .Where(dependency => string.Equals(dependency.Scope, "Test", StringComparison.OrdinalIgnoreCase))
                        .Sum(dependency => dependency.RelationshipCount) ?? 0),
                _ => _context?.TestDependencies.Count ?? 0
            };

            OnPropertyChanged(nameof(ProjectionDescription));
            OnPropertyChanged(nameof(NodeCountLabel));
            LayerFilters = ["All", .. _activeRelations
                .SelectMany(relation => new[] { relation.SourceLayer, relation.TargetLayer })
                .Where(layer => !string.IsNullOrWhiteSpace(layer))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(layer => layer, StringComparer.OrdinalIgnoreCase)];

            if (!LayerFilters.Contains(SelectedLayer, StringComparer.OrdinalIgnoreCase))
                SelectedLayer = "All";

            OnPropertyChanged(nameof(GraphProjectionOptions));
            OnPropertyChanged(nameof(LayerFilters));
            OnPropertyChanged(nameof(GroupingLabel));

            TotalDependencies = _activeRelations.Sum(relation => relation.RelationshipCount);
            ComponentCount = _activeNodes.Count > 0
                ? _activeNodes.Count
                : _activeRelations
                    .SelectMany(relation => new[] { relation.SourceId, relation.TargetId })
                    .Distinct(StringComparer.Ordinal)
                    .Count();
            LayerCount = _activeNodes.Count > 0
                ? _activeNodes.Select(node => node.Group)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count()
                : _activeRelations
                    .SelectMany(relation => new[] { relation.SourceLayer, relation.TargetLayer })
                    .Where(layer => !string.IsNullOrWhiteSpace(layer))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

            RefreshFilter();
        }
        finally
        {
            _refreshingProjection = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedLayer = "All";
        SelectedGraphNode = null;
        RefreshFilter();
    }

    [RelayCommand]
    private void SelectGraphNode(ArchitectureGraphNodeViewModel? node)
    {
        SelectedGraphNode = ReferenceEquals(SelectedGraphNode, node) ? null : node;
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        var selectedNodeId = SelectedGraphNode?.Id;
        var baseDependencies = _activeRelations
            .Where(MatchesBaseFilter)
            .ToArray();

        var graphDependencies = selectedNodeId is null
            ? baseDependencies
            : baseDependencies.Where(dependency =>
                string.Equals(dependency.SourceId, selectedNodeId, StringComparison.Ordinal)
                || string.Equals(dependency.TargetId, selectedNodeId, StringComparison.Ordinal))
                .ToArray();

        BuildGraph(graphDependencies, selectedNodeId);

        if (selectedNodeId is not null)
        {
            var resolvedNode = GraphNodes.FirstOrDefault(node =>
                string.Equals(node.Id, selectedNodeId, StringComparison.Ordinal));

            if (resolvedNode is null)
            {
                SelectedGraphNode = null;
                graphDependencies = baseDependencies;
                        BuildGraph(graphDependencies, null);
            }
            else
            {
                SelectedGraphNode = resolvedNode;
            }
        }

        FilteredDependencies = graphDependencies.Sum(dependency => dependency.RelationshipCount);
    }

    private bool MatchesBaseFilter(GraphRelation dependency)
    {
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

    private void BuildGraph(
        IReadOnlyCollection<GraphRelation> dependencies,
        string? selectedNodeId)
    {
        GraphNodes.Clear();
        GraphEdges.Clear();
        GraphLayerHeaders.Clear();

        if (dependencies.Count == 0 && _activeNodes.Count == 0)
        {
            GraphWidth = 960;
            GraphHeight = 520;
            return;
        }

        var componentLayers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var componentNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in _activeNodes.Where(node =>
                     (selectedNodeId is null || node.Id == selectedNodeId)
                     && MatchesNodeFilter(node)))
        {
            AddLayer(node.Id, node.Name, node.Group, componentLayers, componentNames);
        }

        AddComponentLayers(dependencies, componentLayers, componentNames);

        var componentIds = componentLayers.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var componentLayerNames = componentIds.ToDictionary(
            id => id,
            id => componentLayers[id].Count == 0
                ? "Unclassified"
                : string.Join(" · ", componentLayers[id].OrderBy(layer => layer, StringComparer.OrdinalIgnoreCase)),
            StringComparer.Ordinal);

        var orderedLayers = componentLayerNames.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(layer => string.Equals(layer, "Unclassified", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(layer => layer, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var incomingCounts = dependencies
            .GroupBy(dependency => dependency.TargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(relation => relation.RelationshipCount), StringComparer.Ordinal);

        var outgoingCounts = dependencies
            .GroupBy(dependency => dependency.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(relation => relation.RelationshipCount), StringComparer.Ordinal);

        var nodeById = new Dictionary<string, ArchitectureGraphNodeViewModel>(StringComparer.Ordinal);
        var maxNodesInLayer = 0;

        for (var layerIndex = 0; layerIndex < orderedLayers.Length; layerIndex++)
        {
            var layerName = orderedLayers[layerIndex];
            var layerNodes = componentIds
                .Where(id => string.Equals(componentLayerNames[id], layerName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            maxNodesInLayer = Math.Max(maxNodesInLayer, layerNodes.Length);
            var left = GraphLeftMargin + layerIndex * GraphColumnSpacing;
            GraphLayerHeaders.Add(new ArchitectureGraphLayerHeaderViewModel(layerName, left));

            for (var nodeIndex = 0; nodeIndex < layerNodes.Length; nodeIndex++)
            {
                var id = layerNodes[nodeIndex];
                var name = componentNames[id];
                var node = new ArchitectureGraphNodeViewModel(
                    id,
                    name,
                    layerName,
                    incomingCounts.GetValueOrDefault(id),
                    outgoingCounts.GetValueOrDefault(id),
                    left,
                    GraphTopMargin + nodeIndex * GraphRowSpacing);

                GraphNodes.Add(node);
                nodeById.Add(id, node);
            }
        }

        foreach (var group in dependencies.GroupBy(
            dependency => (Source: dependency.SourceId, Target: dependency.TargetId),
            DependencyPairComparer.Instance)
            .OrderBy(group => group.Key.Source, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Target, StringComparer.Ordinal))
        {
            if (!nodeById.TryGetValue(group.Key.Source, out var source)
                || !nodeById.TryGetValue(group.Key.Target, out var target))
            {
                continue;
            }

            var (left, top, lineGeometry, arrowGeometry) = CreateEdgeGeometry(source, target);
            GraphEdges.Add(new ArchitectureGraphEdgeViewModel(
                source.Name,
                target.Name,
                group.Sum(dependency => dependency.RelationshipCount),
                left,
                top,
                lineGeometry,
                arrowGeometry));
        }

        GraphWidth = Math.Max(
            960,
            GraphLeftMargin * 2
            + (orderedLayers.Length - 1) * GraphColumnSpacing
            + ArchitectureGraphNodeViewModel.NodeWidth
            + 48);

        GraphHeight = Math.Max(
            520,
            GraphTopMargin
            + maxNodesInLayer * GraphRowSpacing
            + 48);
    }

    private bool MatchesNodeFilter(GraphNode node)
    {
        if (!string.Equals(SelectedLayer, "All", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(node.Group, SelectedLayer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(SearchText)
            || Contains(node.Name, SearchText.Trim())
            || Contains(node.Group, SearchText.Trim());
    }

    private static void AddComponentLayers(
        IEnumerable<GraphRelation> dependencies,
        IDictionary<string, HashSet<string>> componentLayers,
        IDictionary<string, string> componentNames)
    {
        foreach (var dependency in dependencies)
        {
            AddLayer(dependency.SourceId, dependency.Source, dependency.SourceLayer, componentLayers, componentNames);
            AddLayer(dependency.TargetId, dependency.Target, dependency.TargetLayer, componentLayers, componentNames);
        }
    }

    private static void AddLayer(
        string componentId,
        string componentName,
        string? layer,
        IDictionary<string, HashSet<string>> componentLayers,
        IDictionary<string, string> componentNames)
    {
        if (!componentLayers.TryGetValue(componentId, out var layers))
        {
            layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            componentLayers.Add(componentId, layers);
            componentNames.Add(componentId, componentName);
        }

        if (!string.IsNullOrWhiteSpace(layer))
            layers.Add(layer.Trim());
    }

    private static (double Left, double Top, Geometry Line, Geometry Arrow) CreateEdgeGeometry(
        ArchitectureGraphNodeViewModel source,
        ArchitectureGraphNodeViewModel target)
    {
        var start = new Point(
            source.Left + ArchitectureGraphNodeViewModel.NodeWidth,
            source.Top + ArchitectureGraphNodeViewModel.NodeHeight / 2);

        var end = new Point(
            target.Left,
            target.Top + ArchitectureGraphNodeViewModel.NodeHeight / 2);

        Point controlOne;
        Point controlTwo;

        if (ReferenceEquals(source, target))
        {
            controlOne = new Point(start.X + 68, start.Y - 58);
            controlTwo = new Point(end.X + 68, end.Y - 58);
            end = new Point(end.X, end.Y - 8);
        }
        else if (end.X > start.X)
        {
            var distance = (end.X - start.X) * 0.45;
            controlOne = new Point(start.X + distance, start.Y);
            controlTwo = new Point(end.X - distance, end.Y);
        }
        else
        {
            controlOne = new Point(start.X + 72, start.Y - 56);
            controlTwo = new Point(end.X - 72, end.Y - 56);
        }

        var curve = new PathFigure { StartPoint = start, IsClosed = false };
        curve.Segments.Add(new BezierSegment(controlOne, controlTwo, end, true));

        var angle = Math.Atan2(end.Y - controlTwo.Y, end.X - controlTwo.X);
        const double arrowLength = 11;
        const double arrowAngle = Math.PI / 7;

        var arrowOne = new Point(
            end.X - arrowLength * Math.Cos(angle - arrowAngle),
            end.Y - arrowLength * Math.Sin(angle - arrowAngle));

        var arrowTwo = new Point(
            end.X - arrowLength * Math.Cos(angle + arrowAngle),
            end.Y - arrowLength * Math.Sin(angle + arrowAngle));

        var arrow = new PathFigure { StartPoint = end, IsClosed = true };
        arrow.Segments.Add(new LineSegment(arrowOne, true));
        arrow.Segments.Add(new LineSegment(arrowTwo, true));

        var lineGeometry = new PathGeometry([curve]);
        var arrowGeometry = new PathGeometry([arrow]);
        var bounds = lineGeometry.Bounds;
        bounds.Union(arrowGeometry.Bounds);

        var localTransform = new TranslateTransform(-bounds.X, -bounds.Y);
        lineGeometry.Transform = localTransform;
        arrowGeometry.Transform = localTransform;

        return (bounds.X, bounds.Y, lineGeometry, arrowGeometry);
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private sealed record GraphNode(string Id, string Name, string Group);

    private sealed record GraphRelation(
        string SourceId,
        string TargetId,
        string Source,
        string Target,
        string SourceLayer,
        string TargetLayer,
        string DependencyType,
        string File,
        int RelationshipCount);

    private sealed class DependencyPairComparer : IEqualityComparer<(string Source, string Target)>
    {
        public static DependencyPairComparer Instance { get; } = new();

        public bool Equals(
            (string Source, string Target) x,
            (string Source, string Target) y) =>
            StringComparer.Ordinal.Equals(x.Source, y.Source)
            && StringComparer.Ordinal.Equals(x.Target, y.Target);

        public int GetHashCode((string Source, string Target) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.Source),
                StringComparer.Ordinal.GetHashCode(value.Target));
    }
}
