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
    private bool _refreshingProjection;

    public DependencyGraphViewModel()
    {
        LayerFilters = ["All"];
        GraphProjectionOptions = ["Build modules"];
    }

    public ObservableCollection<ArchitectureDependencyContext> Dependencies { get; private set; } = new();

    public IReadOnlyList<string> GraphProjectionOptions { get; private set; }

    public string GroupingLabel =>
        GraphProjection == "Architectural components"
            ? "COMPONENT KINDS"
            : "ARCHITECTURE LAYERS";

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
            RefreshProjection();
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
            var componentDependencies = _context?.ProductionComponentDependencies
                .Where(dependency =>
                    !string.IsNullOrWhiteSpace(dependency.SourceComponentId)
                    && !string.IsNullOrWhiteSpace(dependency.TargetComponentId))
                .ToArray() ?? [];

            GraphProjectionOptions = buildDependencies.Length > 0 && componentDependencies.Length > 0
                ? ["Build modules", "Architectural components"]
                : componentDependencies.Length > 0
                    ? ["Architectural components", "Build modules"]
                    : ["Build modules"];

            if (!GraphProjectionOptions.Contains(GraphProjection, StringComparer.OrdinalIgnoreCase))
            {
                GraphProjection = componentDependencies.Length > 0 && buildDependencies.Length == 0
                    ? "Architectural components"
                    : "Build modules";
            }

            _activeRelations = GraphProjection == "Architectural components"
                ? componentDependencies.Select(dependency => new GraphRelation(
                    dependency.SourceComponentName,
                    dependency.TargetComponentName,
                    "Package",
                    "Package",
                    dependency.DependencyType,
                    string.Join(", ", dependency.EvidenceFiles),
                    dependency.RelationshipCount)).ToArray()
                : buildDependencies.Select(dependency => new GraphRelation(
                    dependency.Source,
                    dependency.Target,
                    dependency.SourceLayer,
                    dependency.TargetLayer,
                    dependency.DependencyType,
                    dependency.File,
                    1)).ToArray();

            TestRelationshipCount = GraphProjection == "Architectural components"
                ? _context?.TestComponentDependencies.Sum(dependency => dependency.RelationshipCount) ?? 0
                : _context?.TestDependencies.Count ?? 0;

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
            ComponentCount = _activeRelations
                .SelectMany(relation => new[] { relation.Source, relation.Target })
                .Distinct(StringComparer.Ordinal)
                .Count();
            LayerCount = _activeRelations
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
        var selectedNodeName = SelectedGraphNode?.Name;
        var baseDependencies = _activeRelations
            .Where(MatchesBaseFilter)
            .ToArray();

        var graphDependencies = selectedNodeName is null
            ? baseDependencies
            : baseDependencies.Where(dependency =>
                string.Equals(dependency.Source, selectedNodeName, StringComparison.Ordinal)
                || string.Equals(dependency.Target, selectedNodeName, StringComparison.Ordinal))
                .ToArray();

        BuildGraph(graphDependencies);

        if (selectedNodeName is not null)
        {
            var resolvedNode = GraphNodes.FirstOrDefault(node =>
                string.Equals(node.Name, selectedNodeName, StringComparison.Ordinal));

            if (resolvedNode is null)
            {
                SelectedGraphNode = null;
                graphDependencies = baseDependencies;
                BuildGraph(graphDependencies);
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

    private void BuildGraph(IReadOnlyCollection<GraphRelation> dependencies)
    {
        GraphNodes.Clear();
        GraphEdges.Clear();
        GraphLayerHeaders.Clear();

        if (dependencies.Count == 0)
        {
            GraphWidth = 960;
            GraphHeight = 520;
            return;
        }

        var componentLayers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        AddComponentLayers(dependencies, componentLayers);

        var componentNames = componentLayers.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var componentLayerNames = componentNames.ToDictionary(
            name => name,
            name => componentLayers[name].Count == 0
                ? "Unclassified"
                : string.Join(" · ", componentLayers[name].OrderBy(layer => layer, StringComparer.OrdinalIgnoreCase)),
            StringComparer.Ordinal);

        var orderedLayers = componentLayerNames.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(layer => string.Equals(layer, "Unclassified", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(layer => layer, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var incomingCounts = dependencies
            .GroupBy(dependency => dependency.Target, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(relation => relation.RelationshipCount), StringComparer.Ordinal);

        var outgoingCounts = dependencies
            .GroupBy(dependency => dependency.Source, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(relation => relation.RelationshipCount), StringComparer.Ordinal);

        var nodeByName = new Dictionary<string, ArchitectureGraphNodeViewModel>(StringComparer.Ordinal);
        var maxNodesInLayer = 0;

        for (var layerIndex = 0; layerIndex < orderedLayers.Length; layerIndex++)
        {
            var layerName = orderedLayers[layerIndex];
            var layerNodes = componentNames
                .Where(name => string.Equals(componentLayerNames[name], layerName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            maxNodesInLayer = Math.Max(maxNodesInLayer, layerNodes.Length);
            var left = GraphLeftMargin + layerIndex * GraphColumnSpacing;
            GraphLayerHeaders.Add(new ArchitectureGraphLayerHeaderViewModel(layerName, left));

            for (var nodeIndex = 0; nodeIndex < layerNodes.Length; nodeIndex++)
            {
                var name = layerNodes[nodeIndex];
                var node = new ArchitectureGraphNodeViewModel(
                    name,
                    layerName,
                    incomingCounts.GetValueOrDefault(name),
                    outgoingCounts.GetValueOrDefault(name),
                    left,
                    GraphTopMargin + nodeIndex * GraphRowSpacing);

                GraphNodes.Add(node);
                nodeByName.Add(name, node);
            }
        }

        foreach (var group in dependencies.GroupBy(
            dependency => (dependency.Source, dependency.Target),
            DependencyPairComparer.Instance)
            .OrderBy(group => group.Key.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.Target, StringComparer.OrdinalIgnoreCase))
        {
            if (!nodeByName.TryGetValue(group.Key.Source, out var source)
                || !nodeByName.TryGetValue(group.Key.Target, out var target))
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

    private static void AddComponentLayers(
        IEnumerable<GraphRelation> dependencies,
        IDictionary<string, HashSet<string>> componentLayers)
    {
        foreach (var dependency in dependencies)
        {
            AddLayer(dependency.Source, dependency.SourceLayer, componentLayers);
            AddLayer(dependency.Target, dependency.TargetLayer, componentLayers);
        }
    }

    private static void AddLayer(
        string component,
        string? layer,
        IDictionary<string, HashSet<string>> componentLayers)
    {
        if (!componentLayers.TryGetValue(component, out var layers))
        {
            layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            componentLayers.Add(component, layers);
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

    private sealed record GraphRelation(
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
