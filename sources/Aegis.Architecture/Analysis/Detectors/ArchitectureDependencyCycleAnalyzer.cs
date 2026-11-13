using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Detects strongly connected dependency clusters independently in the
/// language-level code-container and logical architectural graphs.
/// </summary>
internal static class ArchitectureDependencyCycleAnalyzer
{
    private const string CodeContainerProjection = "CodeContainer";
    private const string ArchitecturalComponentProjection = "ArchitecturalComponent";

    public static void Analyze(ProjectArchitectureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ArchitectureComponentCycles.Clear();
        context.ArchitecturalComponentCycles.Clear();

        var codeContainerEdges = context.ArchitectureComponentDependencies
            .Concat(context.ArchitectureComponentInternalDependencies)
            .Select(edge => new DependencyEdge(
                edge.SourceComponentId,
                edge.SourceComponentName,
                edge.TargetComponentId,
                edge.TargetComponentName,
                edge.Scope,
                edge.RelationshipCount,
                edge.ContributingRelationships,
                edge.UnreferencedRelationshipCount));

        var architecturalEdges = context.ArchitecturalComponentDependencies
            .Select(edge => new DependencyEdge(
                edge.SourceComponentId,
                edge.SourceComponentName,
                edge.TargetComponentId,
                edge.TargetComponentName,
                edge.Scope,
                edge.RelationshipCount,
                edge.ContributingRelationships,
                edge.UnreferencedRelationshipCount));

        context.ArchitectureComponentCycles.AddRange(
            FindCycles(CodeContainerProjection, codeContainerEdges));
        context.ArchitecturalComponentCycles.AddRange(
            FindCycles(ArchitecturalComponentProjection, architecturalEdges));
    }

    private static IReadOnlyList<ArchitectureDependencyCycleContext> FindCycles(
        string projection,
        IEnumerable<DependencyEdge> sourceEdges)
    {
        var edges = sourceEdges
            .Where(edge =>
                !string.IsNullOrWhiteSpace(edge.SourceId)
                && !string.IsNullOrWhiteSpace(edge.TargetId))
            .OrderBy(edge => edge.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToArray();

        var results = new List<ArchitectureDependencyCycleContext>();

        foreach (var scopeGroup in edges.GroupBy(edge => edge.Scope, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var scopedEdges = scopeGroup.ToArray();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var edge in scopedEdges)
            {
                names.TryAdd(edge.SourceId, edge.SourceName);
                names.TryAdd(edge.TargetId, edge.TargetName);
            }

            var adjacency = names.Keys.ToDictionary(
                id => id,
                _ => new SortedSet<string>(StringComparer.Ordinal),
                StringComparer.Ordinal);
            var reverseAdjacency = names.Keys.ToDictionary(
                id => id,
                _ => new SortedSet<string>(StringComparer.Ordinal),
                StringComparer.Ordinal);

            foreach (var edge in scopedEdges)
            {
                adjacency[edge.SourceId].Add(edge.TargetId);
                reverseAdjacency[edge.TargetId].Add(edge.SourceId);
            }

            foreach (var componentIds in FindStronglyConnectedComponents(adjacency, reverseAdjacency))
            {
                var members = componentIds.ToHashSet(StringComparer.Ordinal);
                var internalEdges = scopedEdges
                    .Where(edge => members.Contains(edge.SourceId) && members.Contains(edge.TargetId))
                    .ToArray();
                var hasSelfDependency = internalEdges.Any(edge =>
                    string.Equals(edge.SourceId, edge.TargetId, StringComparison.Ordinal));

                if (componentIds.Count == 1 && !hasSelfDependency)
                    continue;

                var relationshipReferences = internalEdges
                    .SelectMany(edge => edge.Relationships)
                    .Where(reference =>
                        !string.IsNullOrWhiteSpace(reference.RunId)
                        && !string.IsNullOrWhiteSpace(reference.RelationshipId))
                    .GroupBy(reference => (reference.RunId, reference.RelationshipId))
                    .Select(group => group.First())
                    .OrderBy(reference => reference.RunId, StringComparer.Ordinal)
                    .ThenBy(reference => reference.RelationshipId, StringComparer.Ordinal)
                    .Select(reference => new SemanticRelationshipReference
                    {
                        RunId = reference.RunId,
                        RelationshipId = reference.RelationshipId
                    })
                    .ToList();

                results.Add(new ArchitectureDependencyCycleContext
                {
                    Projection = projection,
                    Scope = scopeGroup.Key,
                    ComponentIds = componentIds,
                    ComponentNames = componentIds.Select(id => names[id]).ToList(),
                    DependencyEdgeCount = internalEdges.Length,
                    RelationshipCount = internalEdges.Sum(edge => edge.RelationshipCount),
                    ContributingRelationships = relationshipReferences,
                    UnreferencedRelationshipCount = internalEdges.Sum(edge => edge.UnreferencedRelationshipCount),
                    ContainsSelfDependency = hasSelfDependency,
                    IsSingleNodeCycle = componentIds.Count == 1 && hasSelfDependency
                });
            }
        }

        return results
            .OrderBy(cycle => cycle.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(cycle => cycle.ComponentNames[0], StringComparer.OrdinalIgnoreCase)
            .ThenBy(cycle => cycle.ComponentIds[0], StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Iterative Kosaraju traversal avoids recursion depth failures on large
    /// repositories while producing deterministic strongly connected clusters.
    /// </summary>
    private static IReadOnlyList<List<string>> FindStronglyConnectedComponents(
        IReadOnlyDictionary<string, SortedSet<string>> adjacency,
        IReadOnlyDictionary<string, SortedSet<string>> reverseAdjacency)
    {
        var neighboursByNode = adjacency.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var finishOrder = new List<string>(adjacency.Count);

        foreach (var start in adjacency.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!visited.Add(start))
                continue;

            var traversal = new Stack<(string Node, int NextIndex)>();
            traversal.Push((start, 0));

            while (traversal.Count > 0)
            {
                var (node, nextIndex) = traversal.Pop();
                var neighbours = neighboursByNode[node];

                if (nextIndex < neighbours.Length)
                {
                    traversal.Push((node, nextIndex + 1));
                    var neighbour = neighbours[nextIndex];
                    if (visited.Add(neighbour))
                        traversal.Push((neighbour, 0));
                    continue;
                }

                finishOrder.Add(node);
            }
        }

        visited.Clear();
        var components = new List<List<string>>();

        foreach (var start in finishOrder.AsEnumerable().Reverse())
        {
            if (!visited.Add(start))
                continue;

            var component = new List<string>();
            var traversal = new Stack<string>();
            traversal.Push(start);

            while (traversal.Count > 0)
            {
                var node = traversal.Pop();
                component.Add(node);

                foreach (var neighbour in reverseAdjacency[node].Reverse())
                {
                    if (visited.Add(neighbour))
                        traversal.Push(neighbour);
                }
            }

            component.Sort(StringComparer.Ordinal);
            components.Add(component);
        }

        return components;
    }

    private sealed record DependencyEdge(
        string SourceId,
        string SourceName,
        string TargetId,
        string TargetName,
        string Scope,
        int RelationshipCount,
        IReadOnlyList<SemanticRelationshipReference> Relationships,
        int UnreferencedRelationshipCount);
}
