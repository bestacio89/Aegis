using Aegis.Architecture.Analysis.Projection;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;

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

        var rollup = ArchitectureGraphRollup.Apply(projection, null, _graphOptions.Depth);
        BasePackageLabel = string.IsNullOrWhiteSpace(rollup.BasePackage) ? "Base package: package root" : $"Base package: {rollup.BasePackage}";
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
    }

    private void Clear()
    {
        RootRegions.Clear();
        UnclassifiedCodeContainers.Clear();
        BuildModules.Clear();
        ExternalResources.Clear();
        Relationships.Clear();
        UnprojectedEvidence.Clear();
        DeclaredIntegrations.Clear();
        ProjectionDiagnostics.Clear();
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
