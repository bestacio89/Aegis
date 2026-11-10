using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// Presents detected architecture dependencies as both a relationship ledger and
/// a navigable graph. The graph is a projection of detector output; it does not
/// infer, validate, or mutate architectural relationships.
/// </summary>
public sealed partial class ArchitectureExplorerViewModel : ObservableObject
{
    private const double GraphLeftMargin = 32;
    private const double GraphTopMargin = 88;
    private const double GraphColumnSpacing = 292;
    private const double GraphRowSpacing = 104;

    private ICollectionView _dependenciesView;

    public ArchitectureExplorerViewModel()
    {
        LayerFilters = ["All"];
        _dependenciesView = CollectionViewSource.GetDefaultView(Dependencies);
        _dependenciesView.Filter = MatchesFilter;
    }

    public ObservableCollection<ArchitectureDependencyContext> Dependencies { get; private set; } = new();

    public ICollectionView DependenciesView => _dependenciesView;

    public ObservableCollection<ArchitectureGraphNodeViewModel> GraphNodes { get; } = new();

    public ObservableCollection<ArchitectureGraphEdgeViewModel> GraphEdges { get; } = new();

    public ObservableCollection<ArchitectureGraphLayerHeaderViewModel> GraphLayerHeaders { get; } = new();

    public IReadOnlyList<string> LayerFilters { get; private set; }

    [ObservableProperty] private string projectName = "No assessment loaded";
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedLayer = "All";
    [ObservableProperty] private ArchitectureDependencyContext? selectedDependency;
    [ObservableProperty] private ArchitectureGraphNodeViewModel? selectedGraphNode;
    [ObservableProperty] private int totalDependencies;
    [ObservableProperty] private int filteredDependencies;
    [ObservableProperty] private int componentCount;
    [ObservableProperty] private int layerCount;
    [ObservableProperty] private double graphWidth = 960;
    [ObservableProperty] private double graphHeight = 520;

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedLayerChanged(string value) => RefreshFilter();

    partial void OnSelectedDependencyChanged(ArchitectureDependencyContext? value)
    {
        CopySelectedDependencyCommand.NotifyCanExecuteChanged();
        RefreshEdgeHighlights();
    }

    partial void OnSelectedGraphNodeChanged(ArchitectureGraphNodeViewModel? value)
    {
        foreach (var node in GraphNodes)
            node.IsSelected = ReferenceEquals(node, value);

        _dependenciesView.Refresh();

        if (SelectedDependency is not null
            && !_dependenciesView.Contains(SelectedDependency))
        {
            SelectedDependency = null;
        }

        FilteredDependencies = _dependenciesView.Cast<object>().Count();
    }

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
            .ToArray() ?? [];

        SelectedDependency = null;
        SelectedGraphNode = null;
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
        ComponentCount = dependencies
            .SelectMany(dependency => new[] { dependency.Source, dependency.Target })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        LayerCount = dependencies
            .SelectMany(dependency => new[] { dependency.SourceLayer, dependency.TargetLayer })
            .Where(layer => !string.IsNullOrWhiteSpace(layer))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        RefreshFilter();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedLayer = "All";
        SelectedGraphNode = null;
    }

    [RelayCommand]
    private void SelectGraphNode(ArchitectureGraphNodeViewModel? node)
    {
        SelectedGraphNode = ReferenceEquals(SelectedGraphNode, node) ? null : node;
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

    private void RefreshFilter()
    {
        var graphDependencies = Dependencies
            .Where(MatchesBaseFilter)
            .ToArray();

        BuildGraph(graphDependencies);

        if (SelectedGraphNode is not null)
        {
            SelectedGraphNode = GraphNodes.FirstOrDefault(node =>
                string.Equals(node.Name, SelectedGraphNode.Name, StringComparison.OrdinalIgnoreCase));
        }

        _dependenciesView.Refresh();

        if (SelectedDependency is not null
            && !_dependenciesView.Contains(SelectedDependency))
        {
            SelectedDependency = null;
        }

        FilteredDependencies = _dependenciesView.Cast<object>().Count();
        RefreshEdgeHighlights();
    }

    private bool MatchesFilter(object item)
    {
        if (item is not ArchitectureDependencyContext dependency
            || !MatchesBaseFilter(dependency))
        {
            return false;
        }

        return SelectedGraphNode is null
            || string.Equals(dependency.Source, SelectedGraphNode.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(dependency.Target, SelectedGraphNode.Name, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesBaseFilter(ArchitectureDependencyContext dependency)
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

    private void BuildGraph(IReadOnlyCollection<ArchitectureDependencyContext> dependencies)
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

        var componentLayers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        AddComponentLayers(dependencies, componentLayers);

        var componentNames = componentLayers.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var componentLayerNames = componentNames.ToDictionary(
            name => name,
            name => componentLayers[name].Count == 0
                ? "Unclassified"
                : string.Join(" · ", componentLayers[name].OrderBy(layer => layer, StringComparer.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var orderedLayers = componentLayerNames.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(layer => string.Equals(layer, "Unclassified", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(layer => layer, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var incomingCounts = dependencies
            .GroupBy(dependency => dependency.Target, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var outgoingCounts = dependencies
            .GroupBy(dependency => dependency.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var nodeByName = new Dictionary<string, ArchitectureGraphNodeViewModel>(StringComparer.OrdinalIgnoreCase);
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
                group.Count(),
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
        IEnumerable<ArchitectureDependencyContext> dependencies,
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

    private void RefreshEdgeHighlights()
    {
        foreach (var edge in GraphEdges)
        {
            edge.IsHighlighted = SelectedDependency is { } dependency
                && string.Equals(edge.Source, dependency.Source, StringComparison.OrdinalIgnoreCase)
                && string.Equals(edge.Target, dependency.Target, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private sealed class DependencyPairComparer : IEqualityComparer<(string Source, string Target)>
    {
        public static DependencyPairComparer Instance { get; } = new();

        public bool Equals(
            (string Source, string Target) x,
            (string Source, string Target) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Source, y.Source)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Target, y.Target);

        public int GetHashCode((string Source, string Target) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Source),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Target));
    }
}
