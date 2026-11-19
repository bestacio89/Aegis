using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Projection;

/// <summary>
/// Creates a deterministic hierarchical view of package-level graph relationships.
/// It never mutates or replaces the source projection or its leaf evidence.
/// </summary>
public static class ArchitectureGraphRollup
{
    public static ArchitectureGraphRollupResult Apply(
        ProjectArchitectureContext context,
        string? policyOverride,
        int depth)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projection = context.ArchitectureGraphProjection ?? new ArchitectureGraphProjection([], [], []);
        var partitionBasePackages = projection.Nodes
            .Where(IsPackageNode)
            .GroupBy(node => new PackagePartition(GetLanguageFamily(node.Language), node.ModulePath ?? string.Empty))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var applicationPackages = group
                        .Select(node => node.Language)
                        .Where(language => !string.IsNullOrWhiteSpace(language))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(language => ArchitecturePackageBaseResolver.FindSpringBootApplicationPackage(
                            context.SemanticAnalysisRuns,
                            language!,
                            group.Key.ModulePath))
                        .Where(packageName => !string.IsNullOrWhiteSpace(packageName))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    var applicationPackage = applicationPackages.Length == 1
                        ? applicationPackages[0]
                        : null;
                    var packageNodes = group.ToArray();
                    var productionPackageNodes = packageNodes
                        .Where(node => !node.IsTestScoped)
                        .ToArray();
                    var basePackageNodes = productionPackageNodes.Length > 0
                        ? productionPackageNodes
                        : packageNodes;

                    return ArchitecturePackageBaseResolver.Resolve(
                        basePackageNodes.Select(node => node.Name),
                        policyOverride,
                        applicationPackage);
                });

        return Apply(projection, policyOverride, depth, partitionBasePackages);
    }

    public static ArchitectureGraphRollupResult Apply(
        ArchitectureGraphProjection projection,
        string? basePackage,
        int depth) =>
        Apply(projection, basePackage, depth, null);

    private static ArchitectureGraphRollupResult Apply(
        ArchitectureGraphProjection projection,
        string? basePackage,
        int depth,
        IReadOnlyDictionary<PackagePartition, string>? partitionBasePackages)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);

        var packageNodes = projection.Nodes
            .Where(IsPackageNode)
            .ToArray();
        var resolvedBasePackagesByPartition = packageNodes
            .GroupBy(node => new PackagePartition(GetLanguageFamily(node.Language), node.ModulePath ?? string.Empty))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    if (!string.IsNullOrWhiteSpace(basePackage))
                        return ArchitecturePackageBaseResolver.Resolve([], policyOverride: basePackage);

                    if (partitionBasePackages is not null
                        && partitionBasePackages.TryGetValue(group.Key, out var resolvedBasePackage))
                        return resolvedBasePackage;

                    return ArchitecturePackageBaseResolver.Resolve(group.Select(node => node.Name));
                });

        var distinctBasePackages = resolvedBasePackagesByPartition.Values
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var resolvedBasePackage = distinctBasePackages.Length == 1
            ? distinctBasePackages[0]
            : string.Empty;
        var basePackageLabel = distinctBasePackages.Length > 1
            ? $"Base package: resolved per module/language partition ({resolvedBasePackagesByPartition.Count})"
            : $"Base package: {(string.IsNullOrWhiteSpace(resolvedBasePackage) ? "package root" : resolvedBasePackage)}";

        var groupedNodes = new Dictionary<string, NodeBuilder>(StringComparer.Ordinal);
        var nodeGroupByLeafId = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in projection.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            var nodeBasePackage = IsPackageNode(node)
                && resolvedBasePackagesByPartition.TryGetValue(
                    new PackagePartition(GetLanguageFamily(node.Language), node.ModulePath ?? string.Empty),
                    out var partitionBasePackage)
                    ? partitionBasePackage
                    : string.Empty;
            var grouping = CreateNodeGroup(node, nodeBasePackage, depth);
            if (!groupedNodes.TryGetValue(grouping.GroupKey, out var builder))
            {
                builder = new NodeBuilder(
                    grouping.GroupKey,
                    grouping.Name,
                    grouping.Depth,
                    node.Kind,
                    node.Language,
                    node.ComponentKind,
                    node.ArchitecturalRole,
                    node.ModulePath,
                    node.ResourceProvider,
                    node.ResourceIdentity,
                    node.ResourceKind,
                    node.IsTestScoped);
                groupedNodes.Add(grouping.GroupKey, builder);
            }

            builder.AddLanguage(node.Language);
            builder.LeafComponentIds.Add(node.Id);
            nodeGroupByLeafId[node.Id] = grouping.GroupKey;
        }

        var edgeGroups = new Dictionary<EdgeGroupKey, EdgeBuilder>();
        var unresolvedEdges = new List<ArchitectureGraphEdge>();

        foreach (var edge in projection.Edges.OrderBy(edge => edge.Id, StringComparer.Ordinal))
        {
            if (!nodeGroupByLeafId.TryGetValue(edge.SourceNodeId, out var sourceGroup)
                || !nodeGroupByLeafId.TryGetValue(edge.TargetNodeId, out var targetGroup))
            {
                unresolvedEdges.Add(edge);
                continue;
            }

            if (edge.RelationshipCount < 0)
                throw new InvalidOperationException($"Graph edge '{edge.Id}' has a negative relationship count.");

            var relationshipCount = edge.RelationshipCount;
            if (string.Equals(sourceGroup, targetGroup, StringComparison.Ordinal))
            {
                groupedNodes[sourceGroup].AddInternalRelationship(edge.Scope, relationshipCount);
                continue;
            }

            var key = new EdgeGroupKey(sourceGroup, targetGroup, edge.Domain, edge.Scope);
            if (!edgeGroups.TryGetValue(key, out var aggregate))
            {
                aggregate = new EdgeBuilder(key, depth);
                edgeGroups.Add(key, aggregate);
            }

            aggregate.RelationshipCount = checked(aggregate.RelationshipCount + relationshipCount);
            aggregate.RelationshipKinds.UnionWith(edge.RelationshipKinds);
            if (!string.IsNullOrWhiteSpace(edge.ProviderRelationshipKind))
                aggregate.ProviderRelationshipKinds.Add(edge.ProviderRelationshipKind);
            foreach (var evidence in edge.Evidence)
            {
                if (aggregate.TryAddEvidence(evidence))
                    aggregate.Evidence.Add(evidence);
                aggregate.EvidenceFiles.UnionWith(edge.EvidenceFiles.Where(path => !string.IsNullOrWhiteSpace(path)));
            aggregate.EvidenceFiles.UnionWith(evidence.FilePaths.Where(path => !string.IsNullOrWhiteSpace(path)));
            }
            aggregate.LeafEdgeIds.Add(edge.Id);
        }

        var nodes = groupedNodes.Values
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.GroupKey, StringComparer.Ordinal)
            .Select(node => node.Build())
            .ToArray();
        var edges = edgeGroups.Values
            .OrderBy(edge => edge.Key.SourceGroupKey, StringComparer.Ordinal)
            .ThenBy(edge => edge.Key.TargetGroupKey, StringComparer.Ordinal)
            .ThenBy(edge => edge.Key.Domain)
            .ThenBy(edge => edge.Key.Scope, StringComparer.Ordinal)
            .Select(edge => edge.Build())
            .ToArray();

        return new ArchitectureGraphRollupResult(
            resolvedBasePackage,
            depth,
            nodes,
            edges,
            unresolvedEdges,
            projection.UnprojectedEvidence,
            basePackageLabel);
    }

    private static bool IsPackageNode(ArchitectureGraphNode node) =>
        node.Kind == ArchitectureGraphNodeKind.CodeComponent
        && (string.Equals(node.ComponentKind, "Package", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.ComponentKind, "Module", StringComparison.OrdinalIgnoreCase));

    private static string GetLanguageFamily(string? language) =>
        string.Equals(language?.Trim(), "Java", StringComparison.OrdinalIgnoreCase)
        || string.Equals(language?.Trim(), "Kotlin", StringComparison.OrdinalIgnoreCase)
            ? "JVM"
            : language ?? string.Empty;

    private static NodeGrouping CreateNodeGroup(ArchitectureGraphNode node, string basePackage, int depth)
    {
        if (!IsPackageNode(node))
            return new NodeGrouping(LeafGroupKey(node), node.Name, 0);

        var packageName = NormalizePackageName(node.Name);
        var isWithinBase = basePackage.Length == 0
            || string.Equals(packageName, basePackage, StringComparison.Ordinal)
            || packageName.StartsWith(basePackage + ".", StringComparison.Ordinal);

        if (!isWithinBase)
            return new NodeGrouping(LeafGroupKey(node), node.Name, 0);

        var relativeName = basePackage.Length == 0
            ? packageName
            : string.Equals(packageName, basePackage, StringComparison.Ordinal)
                ? string.Empty
                : packageName[(basePackage.Length + 1)..];

        var segments = relativeName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return new NodeGrouping(LeafGroupKey(node), node.Name, 0);

        var groupedName = string.Join(".", segments.Take(depth));
        var groupKey = string.Join(
            "|",
            "package",
            Escape(GetLanguageFamily(node.Language)),
            Escape(node.ModulePath ?? string.Empty),
            Escape(basePackage),
            Escape(groupedName),
            depth.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return new NodeGrouping(groupKey, groupedName, Math.Min(depth, segments.Length));
    }

    private static string LeafGroupKey(ArchitectureGraphNode node) =>
        "leaf|" + Escape(node.Id);

    private static string NormalizePackageName(string name) =>
        string.Join(".", name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private readonly record struct PackagePartition(string LanguageFamily, string ModulePath);

    private readonly record struct NodeGrouping(string GroupKey, string Name, int Depth);

    private readonly record struct EdgeGroupKey(
        string SourceGroupKey,
        string TargetGroupKey,
        ArchitectureGraphEdgeDomain Domain,
        string Scope);

    private sealed class NodeBuilder(
        string groupKey,
        string name,
        int depth,
        ArchitectureGraphNodeKind kind,
        string? language,
        string? componentKind,
        string? architecturalRole,
        string? modulePath,
        string? resourceProvider,
        string? resourceIdentity,
        string? resourceKind,
        bool isTestScoped)
    {
        public string GroupKey { get; } = groupKey;
        public string Name { get; } = name;
        public int Depth { get; } = depth;
        public ArchitectureGraphNodeKind Kind { get; } = kind;
        public string? Language { get; } = language;
        public string? ComponentKind { get; } = componentKind;
        public string? ArchitecturalRole { get; } = architecturalRole;
        public string? ModulePath { get; } = modulePath;
        public string? ResourceProvider { get; } = resourceProvider;
        public string? ResourceIdentity { get; } = resourceIdentity;
        public string? ResourceKind { get; } = resourceKind;
        public bool IsTestScoped { get; } = isTestScoped;
        public List<string> LeafComponentIds { get; } = [];
        private readonly HashSet<string> _languages = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _internalRelationshipCounts = new(StringComparer.Ordinal);

        public void AddLanguage(string? language)
        {
            if (!string.IsNullOrWhiteSpace(language))
                _languages.Add(language);
        }

        public void AddInternalRelationship(string scope, int count)
        {
            _internalRelationshipCounts[scope] = checked(_internalRelationshipCounts.GetValueOrDefault(scope) + count);
        }

        public ArchitectureGraphRollupNode Build()
        {
            var languages = _languages.OrderBy(language => language, StringComparer.Ordinal).ToArray();
            var language = languages.Length > 1
                ? GetLanguageFamily(Language)
                : languages.FirstOrDefault() ?? Language;

            return new ArchitectureGraphRollupNode
            {
                GroupKey = GroupKey,
                Name = Name,
                Depth = Depth,
                Kind = Kind,
                Language = language,
                Languages = Array.AsReadOnly(languages),
            ComponentKind = ComponentKind,
            ArchitecturalRole = ArchitecturalRole,
            ModulePath = ModulePath,
            ResourceProvider = ResourceProvider,
            ResourceIdentity = ResourceIdentity,
            ResourceKind = ResourceKind,
            IsTestScoped = IsTestScoped,
            LeafComponentIds = Array.AsReadOnly(LeafComponentIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()),
                InternalRelationshipCounts = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                    new Dictionary<string, int>(_internalRelationshipCounts, StringComparer.Ordinal))
            };
        }
    }

    private sealed class EdgeBuilder(EdgeGroupKey key, int groupDepth)
    {
        public EdgeGroupKey Key { get; } = key;
        public int GroupDepth { get; } = groupDepth;
        public int RelationshipCount { get; set; }
        public HashSet<ArchitectureRelationshipKind> RelationshipKinds { get; } = [];
        public HashSet<string> ProviderRelationshipKinds { get; } = new(StringComparer.Ordinal);
        public List<ArchitectureGraphEvidenceReference> Evidence { get; } = [];
        public HashSet<string> EvidenceFiles { get; } = new(StringComparer.Ordinal);
        private readonly HashSet<(ArchitectureGraphEvidenceDomain Domain, int OccurrenceIndex)> _evidenceOccurrences = [];
        public HashSet<string> LeafEdgeIds { get; } = new(StringComparer.Ordinal);

        public bool TryAddEvidence(ArchitectureGraphEvidenceReference evidence) =>
            _evidenceOccurrences.Add((evidence.Domain, evidence.OccurrenceIndex));

        public ArchitectureGraphRollupEdge Build() => new()
        {
            SourceGroupKey = Key.SourceGroupKey,
            TargetGroupKey = Key.TargetGroupKey,
            Domain = Key.Domain,
            Scope = Key.Scope,
            GroupDepth = GroupDepth,
            RelationshipCount = RelationshipCount,
            RelationshipKinds = Array.AsReadOnly(RelationshipKinds.OrderBy(kind => kind).ToArray()),
            ProviderRelationshipKinds = Array.AsReadOnly(ProviderRelationshipKinds.OrderBy(kind => kind, StringComparer.Ordinal).ToArray()),
            Evidence = Array.AsReadOnly(Evidence.OrderBy(item => item.Domain).ThenBy(item => item.OccurrenceIndex).ToArray()),
            EvidenceFiles = Array.AsReadOnly(EvidenceFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray()),
            LeafEdgeIds = Array.AsReadOnly(LeafEdgeIds.OrderBy(id => id, StringComparer.Ordinal).ToArray())
        };
    }
}
