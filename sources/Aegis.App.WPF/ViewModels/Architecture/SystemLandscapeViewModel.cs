using Aegis.Architecture.Analysis.Projection;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace Aegis.Wpf.ViewModels.Architecture;

public sealed partial class SystemLandscapeViewModel : ObservableObject
{
    private readonly ArchitectureGraphViewOptions _graphOptions;
    private ProjectArchitectureContext? _context;
    public SystemLandscapeViewModel(ArchitectureGraphViewOptions? graphOptions = null)
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
    }
    public int RollupDepth { get => _graphOptions.Depth; set => _graphOptions.Depth = value; }
    public string BasePackageLabel { get; private set; } = "Base package: not available";

    [ObservableProperty] private string projectName = "No assessment loaded";
    [ObservableProperty] private SystemLandscapeTopologyNodeViewModel? selectedTopologyNode;
    [ObservableProperty] private SystemLandscapeRelationshipViewModel? selectedRelationship;
    [ObservableProperty] private string selectedDomain = "All";
    [ObservableProperty] private string selectedScope = "All";
    [ObservableProperty] private int logicalRegionCount;
    [ObservableProperty] private int codeContainerCount;
    [ObservableProperty] private int buildModuleCount;
    [ObservableProperty] private int externalResourceCount;
    [ObservableProperty] private int relationshipCount;
    [ObservableProperty] private int unprojectedEvidenceCount;
    [ObservableProperty] private int declaredIntegrationCount;

    public ObservableCollection<SystemLandscapeRegionViewModel> RootRegions { get; } = [];
    public ObservableCollection<SystemLandscapeCodeContainerViewModel> UnclassifiedCodeContainers { get; } = [];
    public ObservableCollection<SystemLandscapeElementViewModel> BuildModules { get; } = [];
    public ObservableCollection<SystemLandscapeElementViewModel> ExternalResources { get; } = [];
    public ObservableCollection<SystemLandscapeRelationshipViewModel> Relationships { get; } = [];
    public ObservableCollection<ArchitectureGraphUnprojectedEvidence> UnprojectedEvidence { get; } = [];
    public ObservableCollection<ArchitectureIntegrationContext> DeclaredIntegrations { get; } = [];
    public ObservableCollection<string> ProjectionDiagnostics { get; } = [];
    public ObservableCollection<SystemLandscapeTopologyNodeViewModel> TopologyNodes { get; } = [];
    public ObservableCollection<SystemLandscapeTopologyEdgeViewModel> TopologyEdges { get; } = [];
    public ObservableCollection<SystemLandscapeRelationshipViewModel> VisibleRelationships { get; } = [];
    public IReadOnlyList<string> DomainOptions { get; private set; } = ["All"];
    public IReadOnlyList<string> ScopeOptions { get; private set; } = ["All"];
    public string SelectionStatus => SelectedRelationship is not null
        ? string.Concat("Selected ", SelectedRelationship.Domain, " relationship: ", SelectedRelationship.Connection, " (", SelectedRelationship.Scope, ").")
        : SelectedTopologyNode is not null
            ? string.Concat("Selected ", SelectedTopologyNode.Category, ": ", SelectedTopologyNode.Name, ".")
            : "No topology element or relationship is selected.";
    public bool HasActiveInteractionFilter =>
        !string.Equals(SelectedDomain, "All", StringComparison.Ordinal)
        || !string.Equals(SelectedScope, "All", StringComparison.Ordinal);
    public Size TopologyCanvasSize { get; private set; } = new(1, 1);

    partial void OnSelectedTopologyNodeChanged(SystemLandscapeTopologyNodeViewModel? value)
    {
        OnPropertyChanged(nameof(SelectionStatus));
        RefreshInteractionState();
    }

    partial void OnSelectedRelationshipChanged(SystemLandscapeRelationshipViewModel? value)
    {
        if (value is not null)
            SelectedTopologyNode = value.IsInternalToGroup
                ? TopologyNodes.FirstOrDefault(node => node.InternalRelationships.Contains(value))
                : null;
        OnPropertyChanged(nameof(SelectionStatus));
        RefreshInteractionState();
    }

    partial void OnSelectedDomainChanged(string value) => RefreshInteractionState();
    partial void OnSelectedScopeChanged(string value) => RefreshInteractionState();

    public void Update(ProjectArchitectureContext? context)
    {
        _context = context;
        Clear();
        ProjectName = string.IsNullOrWhiteSpace(context?.ProjectName) ? "No assessment loaded" : context.ProjectName;
        if (context is null)
            return;

        var projection = context.ArchitectureGraphProjection;
        if (projection is null)
        {
            ProjectionDiagnostics.Add("No unified graph projection is attached. Relationships are not reconstructed from legacy collections.");
            CopyDeclaredIntegrations(context);
            return;
        }

        var rollup = ArchitectureGraphRollup.Apply(context, null, _graphOptions.Depth);
        BasePackageLabel = rollup.BasePackageLabel;
        OnPropertyChanged(nameof(BasePackageLabel));

        var nodeGroups = projection.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var nodes = nodeGroups.Where(pair => pair.Value.Length == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.Ordinal);

        foreach (var duplicate in nodeGroups.Where(pair => pair.Value.Length > 1))
            ProjectionDiagnostics.Add($"Graph node '{duplicate.Key}' has duplicate identities and is excluded from presentation ownership.");

        var componentGroups = context.ArchitecturalComponents.Where(component => !string.IsNullOrWhiteSpace(component.Id))
            .GroupBy(component => component.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var builders = nodes.Values.Where(node => node.Kind == ArchitectureGraphNodeKind.LogicalComponent)
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .Select(node =>
            {
                var matches = componentGroups.GetValueOrDefault(node.Id) ?? [];
                var component = matches.Length == 1 ? matches[0] : null;
                if (matches.Length > 1)
                    ProjectionDiagnostics.Add($"Logical component '{node.Id}' has ambiguous metadata; parent information is not used.");
                return new RegionBuilder(node.Id, node.Name, component?.Role ?? "Unclassified",
                    component?.Description ?? string.Empty, component?.ParentId);
            })
            .ToDictionary(builder => builder.Id, StringComparer.Ordinal);

        var cycles = FindCycles(builders);
        foreach (var id in cycles)
            ProjectionDiagnostics.Add($"Logical component '{id}' participates in a parent cycle and is rendered as a root.");

        foreach (var builder in builders.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (cycles.Contains(builder.Id) || string.IsNullOrWhiteSpace(builder.ParentId))
                continue;
            if (!builders.TryGetValue(builder.ParentId, out var parent))
            {
                builder.HasMissingParent = true;
                ProjectionDiagnostics.Add($"Logical component '{builder.Id}' references missing parent '{builder.ParentId}' and is rendered as a root.");
                continue;
            }
            if (!cycles.Contains(parent.Id) && !string.Equals(builder.Id, parent.Id, StringComparison.Ordinal))
                parent.Children.Add(builder);
        }

        var memberships = context.ArchitecturalComponentMemberships
            .Where(item => !string.IsNullOrWhiteSpace(item.CodeComponentId))
            .GroupBy(item => item.CodeComponentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var codeNodes = nodes.Values.Where(node => node.Kind == ArchitectureGraphNodeKind.CodeComponent)
            .OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();

        foreach (var node in codeNodes)
        {
            var matches = memberships.GetValueOrDefault(node.Id) ?? [];
            var membership = matches.Length == 1 ? matches[0] : null;
            if (matches.Length > 1)
            {
                UnclassifiedCodeContainers.Add(CreateCode(node, "Ambiguous", 0, "Multiple membership records exist."));
                ProjectionDiagnostics.Add($"Code container '{node.Id}' has multiple membership records; no logical owner is selected.");
                continue;
            }

            if (membership is null
                || membership.ClassificationSource == ArchitectureClassificationSource.Unclassified
                || string.IsNullOrWhiteSpace(membership.ArchitecturalComponentId)
                || !double.IsFinite(membership.Confidence)
                || membership.Confidence is < 0 or > 1
                || !builders.TryGetValue(membership.ArchitecturalComponentId, out var owner))
            {
                UnclassifiedCodeContainers.Add(CreateCode(node,
                    membership?.ClassificationSource.ToString() ?? "Unclassified",
                    membership is not null && double.IsFinite(membership.Confidence) ? membership.Confidence : 0,
                    membership?.Explanation ?? "No unique logical membership was discovered."));
                continue;
            }

            owner.CodeContainers.Add(CreateCode(node, membership.ClassificationSource.ToString(),
                membership.Confidence, membership.Explanation));
        }

        var built = new Dictionary<string, SystemLandscapeRegionViewModel>(StringComparer.Ordinal);
        foreach (var builder in builders.Values)
            BuildRegion(builder, built);

        foreach (var builder in builders.Values
            .Where(item => item.ParentId is null || !builders.ContainsKey(item.ParentId)
                || cycles.Contains(item.Id) || cycles.Contains(item.ParentId))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (built.TryGetValue(builder.Id, out var region))
                RootRegions.Add(region);
        }

        foreach (var node in nodes.Values.Where(node => node.Kind == ArchitectureGraphNodeKind.BuildModule)
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Id, StringComparer.Ordinal))
        {
            var matches = context.Modules.Where(module => PathsMatch(module.Path, node.ModulePath)).Take(2).ToArray();
            var module = matches.Length == 1 ? matches[0] : null;
            var detail = module is null ? node.ModulePath ?? "Build module"
                : string.Join(" · ", new[] { module.Type, module.ProjectKind, string.Join(", ", module.Languages) }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            BuildModules.Add(new SystemLandscapeElementViewModel(node.Id, node.Name, "Build module", detail,
                node.IsTestScoped || module?.IsTestProject == true || module?.IsTestSupport == true));
            if (matches.Length > 1)
                ProjectionDiagnostics.Add($"Build module '{node.Id}' matches multiple module records; metadata is not attached.");
        }

        foreach (var node in nodes.Values.Where(node => node.Kind == ArchitectureGraphNodeKind.ExternalResource)
            .OrderBy(node => node.ResourceKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Id, StringComparer.Ordinal))
        {
            var detail = string.Join(" · ", new[] { node.ResourceKind, node.ResourceProvider, node.ResourceIdentity }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            ExternalResources.Add(new SystemLandscapeElementViewModel(node.Id, node.Name,
                "External resource", detail, node.IsTestScoped));
        }

        var rollupNodes = rollup.Nodes.ToDictionary(node => node.GroupKey, StringComparer.Ordinal);
        foreach (var edge in rollup.Edges.OrderBy(edge => edge.Domain).ThenBy(edge => edge.SourceGroupKey, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetGroupKey, StringComparer.Ordinal).ThenBy(edge => edge.Scope, StringComparer.Ordinal))
        {
            if (!rollupNodes.TryGetValue(edge.SourceGroupKey, out var source) || !rollupNodes.TryGetValue(edge.TargetGroupKey, out var target))
            {
                ProjectionDiagnostics.Add($"Rolled-up graph edge '{edge.SourceGroupKey}->{edge.TargetGroupKey}' has a missing endpoint and is omitted from the interaction list.");
                continue;
            }
            var projectedEdge = new ArchitectureGraphEdge
            {
                Id = $"{edge.SourceGroupKey}->{edge.TargetGroupKey}:{edge.Domain}:{edge.Scope}",
                SourceNodeId = edge.SourceGroupKey, TargetNodeId = edge.TargetGroupKey, Domain = edge.Domain, Scope = edge.Scope,
                RelationshipCount = edge.RelationshipCount, ProviderRelationshipKind = string.Join(", ", edge.ProviderRelationshipKinds),
                RelationshipKinds = edge.RelationshipKinds, Evidence = edge.Evidence
            };
            Relationships.Add(new SystemLandscapeRelationshipViewModel(projectedEdge, source.Name, target.Name)
            {
                ContributingLeafEdgeIds = edge.LeafEdgeIds
            });
        }

        BuildTopology(projection, rollup, nodes, builders, memberships, rollupNodes, cycles);

        foreach (var item in projection.UnprojectedEvidence.OrderBy(item => item.Evidence.Domain)
            .ThenBy(item => item.Evidence.OccurrenceIndex).ThenBy(item => item.Reason))
            UnprojectedEvidence.Add(item);

        CopyDeclaredIntegrations(context);
        LogicalRegionCount = RootRegions.Sum(CountRegions);
        CodeContainerCount = codeNodes.Length;
        BuildModuleCount = BuildModules.Count;
        ExternalResourceCount = ExternalResources.Count;
        RelationshipCount = Relationships.Count;
        UnprojectedEvidenceCount = UnprojectedEvidence.Count;
        RefreshInteractionOptions();
        RefreshInteractionState();
    }

    [RelayCommand]
    private void SelectTopologyNode(SystemLandscapeTopologyNodeViewModel? node)
    {
        SelectedRelationship = null;
        SelectedTopologyNode = ReferenceEquals(SelectedTopologyNode, node) ? null : node;
    }

    [RelayCommand]
    private void SelectRelationship(SystemLandscapeRelationshipViewModel? relationship) =>
        SelectedRelationship = ReferenceEquals(SelectedRelationship, relationship) ? null : relationship;

    [RelayCommand]
    private void ClearInteraction()
    {
        SelectedRelationship = null;
        SelectedTopologyNode = null;
        SelectedDomain = "All";
        SelectedScope = "All";
        RefreshInteractionState();
    }

    private void RefreshInteractionOptions()
    {
        DomainOptions = ["All", .. Relationships.Select(item => item.Domain)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)];
        ScopeOptions = ["All", .. Relationships.Select(item => item.Scope)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)];
        if (!DomainOptions.Contains(SelectedDomain, StringComparer.Ordinal))
            SelectedDomain = "All";
        if (!ScopeOptions.Contains(SelectedScope, StringComparer.Ordinal))
            SelectedScope = "All";
        OnPropertyChanged(nameof(DomainOptions));
        OnPropertyChanged(nameof(ScopeOptions));
        OnPropertyChanged(nameof(HasActiveInteractionFilter));
    }

    private void RefreshInteractionState()
    {
        var visible = Relationships
            .Where(MatchesInteractionFilters)
            .Where(relationship => SelectedTopologyNode is null
                || RelationshipTouchesNode(relationship, SelectedTopologyNode))
            .ToArray();
        VisibleRelationships.Clear();
        foreach (var relationship in visible)
            VisibleRelationships.Add(relationship);

        if (SelectedRelationship is not null
            && !visible.Any(item => ReferenceEquals(item, SelectedRelationship)))
            SelectedRelationship = null;

        var hasSelection = SelectedRelationship is not null || SelectedTopologyNode is not null;
        foreach (var edge in TopologyEdges)
        {
            var filterMatch = MatchesInteractionFilters(edge.Domain, edge.Scope);
            var relationshipMatch = SelectedRelationship is not null
                && RelationshipMatchesTopologyEdge(SelectedRelationship, edge);
            var nodeMatch = SelectedTopologyNode is not null
                && (string.Equals(edge.SourceNodeId, SelectedTopologyNode.Id, StringComparison.Ordinal)
                    || string.Equals(edge.TargetNodeId, SelectedTopologyNode.Id, StringComparison.Ordinal));
            edge.IsSelected = filterMatch && relationshipMatch;
            edge.IsHighlighted = filterMatch && (relationshipMatch || nodeMatch);
            edge.IsDimmed = !filterMatch || (hasSelection && !relationshipMatch && !nodeMatch);
        }

        foreach (var node in TopologyNodes)
        {
            var relationshipMatch = SelectedRelationship is not null
                && (string.Equals(node.Id, SelectedRelationship.EvidenceEdge.SourceNodeId, StringComparison.Ordinal)
                    || string.Equals(node.Id, SelectedRelationship.EvidenceEdge.TargetNodeId, StringComparison.Ordinal)
                    || node.InternalRelationships.Contains(SelectedRelationship));
            var nodeMatch = SelectedTopologyNode is not null && ReferenceEquals(node, SelectedTopologyNode);
            node.IsSelected = nodeMatch;
            node.IsRelated = relationshipMatch
                || (SelectedTopologyNode is not null && TopologyEdges.Any(edge => edge.IsHighlighted
                    && (edge.SourceNodeId == node.Id || edge.TargetNodeId == node.Id)));
            node.IsDimmed = hasSelection && !nodeMatch && !node.IsRelated;
        }

        OnPropertyChanged(nameof(SelectionStatus));
        OnPropertyChanged(nameof(HasActiveInteractionFilter));
    }

    private bool MatchesInteractionFilters(SystemLandscapeRelationshipViewModel relationship) =>
        MatchesInteractionFilters(relationship.Domain, relationship.Scope);

    private bool MatchesInteractionFilters(string domain, string scope) =>
        (string.Equals(SelectedDomain, "All", StringComparison.Ordinal)
            || string.Equals(domain, SelectedDomain, StringComparison.Ordinal))
        && (string.Equals(SelectedScope, "All", StringComparison.Ordinal)
            || string.Equals(scope, SelectedScope, StringComparison.Ordinal));

    private static bool RelationshipTouchesNode(
        SystemLandscapeRelationshipViewModel relationship,
        SystemLandscapeTopologyNodeViewModel node) =>
        node.InternalRelationships.Contains(relationship)
        || node.LeafComponentIds.Contains(relationship.EvidenceEdge.SourceNodeId, StringComparer.Ordinal)
        || node.LeafComponentIds.Contains(relationship.EvidenceEdge.TargetNodeId, StringComparer.Ordinal)
        || string.Equals(node.Id, relationship.EvidenceEdge.SourceNodeId, StringComparison.Ordinal)
        || string.Equals(node.Id, relationship.EvidenceEdge.TargetNodeId, StringComparison.Ordinal);

    private static bool RelationshipMatchesTopologyEdge(
        SystemLandscapeRelationshipViewModel relationship,
        SystemLandscapeTopologyEdgeViewModel edge) =>
        !relationship.IsInternalToGroup
        && string.Equals(relationship.EvidenceEdge.SourceNodeId, edge.SourceNodeId, StringComparison.Ordinal)
        && string.Equals(relationship.EvidenceEdge.TargetNodeId, edge.TargetNodeId, StringComparison.Ordinal)
        && string.Equals(relationship.Domain, edge.Domain, StringComparison.Ordinal)
        && string.Equals(relationship.Scope, edge.Scope, StringComparison.Ordinal)
        && relationship.ContributingLeafEdgeIds.Any(edge.LeafEdgeIds.Contains);

    private void BuildTopology(
        ArchitectureGraphProjection projection,
        ArchitectureGraphRollupResult rollup,
        IReadOnlyDictionary<string, ArchitectureGraphNode> sourceNodes,
        IReadOnlyDictionary<string, RegionBuilder> regions,
        IReadOnlyDictionary<string, ArchitecturalComponentMembership[]> memberships,
        IReadOnlyDictionary<string, ArchitectureGraphRollupNode> rollupNodes,
        IReadOnlySet<string> cyclicRegions)
    {
        var regionGroupByLeafId = rollup.Nodes
            .Where(node => node.Kind == ArchitectureGraphNodeKind.LogicalComponent)
            .SelectMany(node => node.LeafComponentIds.Select(leafId => (leafId, node.GroupKey)))
            .GroupBy(item => item.leafId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().GroupKey, StringComparer.Ordinal);

        foreach (var region in rollup.Nodes
            .Where(node => node.Kind == ArchitectureGraphNodeKind.LogicalComponent)
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.GroupKey, StringComparer.Ordinal))
        {
            var leafId = region.LeafComponentIds.Count == 1 ? region.LeafComponentIds[0] : null;
            var builder = leafId is not null && regions.TryGetValue(leafId, out var value) ? value : null;
            string? parentRegionId = null;
            if (builder?.ParentId is not null
                && !cyclicRegions.Contains(builder.Id)
                && !cyclicRegions.Contains(builder.ParentId))
                regionGroupByLeafId.TryGetValue(builder.ParentId, out parentRegionId);

            TopologyNodes.Add(new SystemLandscapeTopologyNodeViewModel
            {
                Id = region.GroupKey,
                Name = region.Name,
                Category = builder?.Role ?? "Logical region",
                Detail = builder?.Description ?? string.Empty,
                ParentRegionId = parentRegionId,
                HierarchyDepth = builder is null ? 0 : GetRegionDepth(builder, regions),
                IsRegion = true,
                LeafComponentIds = region.LeafComponentIds
            });
        }

        foreach (var group in rollup.Nodes.Where(node => node.Kind == ArchitectureGraphNodeKind.CodeComponent)
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.GroupKey, StringComparer.Ordinal))
        {
            var validOwnerIds = new HashSet<string>(StringComparer.Ordinal);
            var valid = group.LeafComponentIds.Count > 0
                && group.LeafComponentIds.Distinct(StringComparer.Ordinal).Count() == group.LeafComponentIds.Count;
            foreach (var leafId in group.LeafComponentIds)
            {
                if (!sourceNodes.ContainsKey(leafId))
                {
                    valid = false;
                    break;
                }

                var records = memberships.GetValueOrDefault(leafId) ?? [];
                if (records.Length != 1)
                {
                    valid = false;
                    break;
                }

                var membership = records[0];
                if (membership.ClassificationSource == ArchitectureClassificationSource.Unclassified
                    || string.IsNullOrWhiteSpace(membership.ArchitecturalComponentId)
                    || !double.IsFinite(membership.Confidence)
                    || membership.Confidence is < 0 or > 1
                    || !regions.ContainsKey(membership.ArchitecturalComponentId))
                {
                    valid = false;
                    break;
                }

                validOwnerIds.Add(membership.ArchitecturalComponentId);
            }

            valid &= validOwnerIds.Count == 1;
            var ownerId = valid ? validOwnerIds.Single() : null;
            var ownerGroupId = ownerId is not null && regionGroupByLeafId.TryGetValue(ownerId, out var groupId)
                ? groupId
                : null;
            var detail = string.Join(", ", group.Languages);
            var node = new SystemLandscapeTopologyNodeViewModel
            {
                Id = group.GroupKey,
                Name = group.Name,
                Category = valid && ownerGroupId is not null ? group.ComponentKind ?? "Code container" : "Unclassified code container",
                Detail = detail,
                ParentRegionId = ownerGroupId,
                IsUnclassified = !valid || ownerGroupId is null,
                LeafComponentIds = group.LeafComponentIds
            };
            TopologyNodes.Add(node);

            var leafIds = group.LeafComponentIds.ToHashSet(StringComparer.Ordinal);
            var internalRelationships = projection.Edges
                .Where(edge => leafIds.Contains(edge.SourceNodeId) && leafIds.Contains(edge.TargetNodeId))
                .OrderBy(edge => edge.Id, StringComparer.Ordinal)
                .Select(edge =>
                {
                    var sourceName = sourceNodes.TryGetValue(edge.SourceNodeId, out var sourceNode)
                        ? sourceNode.Name : edge.SourceNodeId;
                    var targetName = sourceNodes.TryGetValue(edge.TargetNodeId, out var targetNode)
                        ? targetNode.Name : edge.TargetNodeId;
                    return new SystemLandscapeRelationshipViewModel(edge, sourceName, targetName)
                    {
                        ContributingLeafEdgeIds = [edge.Id],
                        IsInternalToGroup = true
                    };
                })
                .ToArray();
            if (internalRelationships.Length > 0)
            {
                node.InternalRelationships = internalRelationships;
                foreach (var relationship in internalRelationships)
                    Relationships.Add(relationship);
            }
        }

        foreach (var group in rollup.Nodes.Where(node =>
            node.Kind is ArchitectureGraphNodeKind.BuildModule or ArchitectureGraphNodeKind.ExternalResource)
            .OrderBy(node => node.Kind)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.GroupKey, StringComparer.Ordinal))
        {
            TopologyNodes.Add(new SystemLandscapeTopologyNodeViewModel
            {
                Id = group.GroupKey,
                Name = group.Name,
                Category = group.Kind == ArchitectureGraphNodeKind.BuildModule ? "Build boundary" : "External resource",
                Detail = string.Join(" · ", new[] { group.ResourceKind, group.ResourceProvider, group.ResourceIdentity, group.ModulePath }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
                LeafComponentIds = group.LeafComponentIds
            });
        }

        foreach (var unresolved in rollup.UnresolvedEdges.OrderBy(edge => edge.Id, StringComparer.Ordinal))
            ProjectionDiagnostics.Add($"Graph edge '{unresolved.Id}' has an unresolved roll-up endpoint and is not drawn.");

        foreach (var edge in rollup.Edges
            .OrderBy(item => item.Domain)
            .ThenBy(item => item.SourceGroupKey, StringComparer.Ordinal)
            .ThenBy(item => item.TargetGroupKey, StringComparer.Ordinal)
            .ThenBy(item => item.Scope, StringComparer.Ordinal))
        {
            if (!rollupNodes.ContainsKey(edge.SourceGroupKey)
                || !rollupNodes.ContainsKey(edge.TargetGroupKey))
            {
                ProjectionDiagnostics.Add($"Rolled-up topology edge '{edge.SourceGroupKey}->{edge.TargetGroupKey}' has a missing endpoint and is not drawn.");
                continue;
            }

            TopologyEdges.Add(new SystemLandscapeTopologyEdgeViewModel
            {
                Id = $"{edge.SourceGroupKey}|{edge.TargetGroupKey}|{edge.Domain}|{edge.Scope}",
                SourceNodeId = edge.SourceGroupKey,
                TargetNodeId = edge.TargetGroupKey,
                Domain = edge.Domain.ToString(),
                Scope = edge.Scope,
                RelationshipCount = edge.RelationshipCount,
                LeafEdgeIds = edge.LeafEdgeIds,
                Evidence = edge.Evidence
            });
        }

        var ordered = TopologyNodes.OrderBy(node => node.IsRegion ? 0 : 1)
            .ThenBy(node => node.HierarchyDepth)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        TopologyNodes.Clear();
        foreach (var node in ordered)
            TopologyNodes.Add(node);

        TopologyCanvasSize = SystemLandscapeTopologyLayout.Arrange(TopologyNodes, TopologyEdges);
        OnPropertyChanged(nameof(TopologyCanvasSize));
    }

    private static int GetRegionDepth(
        RegionBuilder region,
        IReadOnlyDictionary<string, RegionBuilder> regions)
    {
        var depth = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal) { region.Id };
        var current = region;
        while (!string.IsNullOrWhiteSpace(current.ParentId)
            && regions.TryGetValue(current.ParentId, out var parent)
            && visited.Add(parent.Id))
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    private void Clear()
    {
        RootRegions.Clear();
        UnclassifiedCodeContainers.Clear();
        BuildModules.Clear();
        ExternalResources.Clear();
        Relationships.Clear();
        VisibleRelationships.Clear();
        UnprojectedEvidence.Clear();
        DeclaredIntegrations.Clear();
        ProjectionDiagnostics.Clear();
        SelectedRelationship = null;
        SelectedTopologyNode = null;
        SelectedDomain = "All";
        SelectedScope = "All";
        TopologyNodes.Clear();
        TopologyEdges.Clear();
        TopologyCanvasSize = new Size(1, 1);
        OnPropertyChanged(nameof(TopologyCanvasSize));
        DomainOptions = ScopeOptions = ["All"];
        OnPropertyChanged(nameof(DomainOptions));
        OnPropertyChanged(nameof(ScopeOptions));
        OnPropertyChanged(nameof(SelectionStatus));
        OnPropertyChanged(nameof(HasActiveInteractionFilter));
        LogicalRegionCount = CodeContainerCount = BuildModuleCount = ExternalResourceCount = 0;
        RelationshipCount = UnprojectedEvidenceCount = DeclaredIntegrationCount = 0;
    }

    private void CopyDeclaredIntegrations(ProjectArchitectureContext context)
    {
        foreach (var item in context.ApplicationIntegrations.OrderBy(item => item.SourceModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TargetModuleName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id))
            DeclaredIntegrations.Add(item);
        DeclaredIntegrationCount = DeclaredIntegrations.Count;
    }

    private static SystemLandscapeCodeContainerViewModel CreateCode(
        ArchitectureGraphNode node, string source, double confidence, string explanation) =>
        new(node.Id, node.Name, node.Language ?? "Unknown", node.ComponentKind ?? "Code component",
            source, confidence, explanation);

    private static bool PathsMatch(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static HashSet<string> FindCycles(IReadOnlyDictionary<string, RegionBuilder> regions)
    {
        var cyclic = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in regions.Values)
        {
            var path = new List<string>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var current = region;
            while (!string.IsNullOrWhiteSpace(current.ParentId) && regions.TryGetValue(current.ParentId, out var parent))
            {
                if (positions.TryGetValue(current.Id, out var start))
                {
                    foreach (var id in path.Skip(start))
                        cyclic.Add(id);
                    break;
                }
                positions[current.Id] = path.Count;
                path.Add(current.Id);
                current = parent;
            }
            if (positions.TryGetValue(current.Id, out var cycleStart))
            {
                foreach (var id in path.Skip(cycleStart))
                    cyclic.Add(id);
            }
        }
        return cyclic;
    }

    private static SystemLandscapeRegionViewModel BuildRegion(
        RegionBuilder builder, IDictionary<string, SystemLandscapeRegionViewModel> cache)
    {
        if (cache.TryGetValue(builder.Id, out var existing))
            return existing;
        var children = builder.Children.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal).Select(item => BuildRegion(item, cache)).ToArray();
        var region = new SystemLandscapeRegionViewModel(builder.Id, builder.Name, builder.Role,
            builder.Description, children, builder.CodeContainers.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            builder.HasMissingParent);
        cache.Add(builder.Id, region);
        return region;
    }

    private static int CountRegions(SystemLandscapeRegionViewModel region) =>
        1 + region.Children.Sum(CountRegions);

    private sealed class RegionBuilder(string id, string name, string role, string description, string? parentId)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string Role { get; } = role;
        public string Description { get; } = description;
        public string? ParentId { get; } = parentId;
        public bool HasMissingParent { get; set; }
        public List<RegionBuilder> Children { get; } = [];
        public List<SystemLandscapeCodeContainerViewModel> CodeContainers { get; } = [];
    }
}
