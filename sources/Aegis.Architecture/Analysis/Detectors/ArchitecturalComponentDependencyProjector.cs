using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Aggregates language-level semantic dependency edges into the logical
/// architectural component graph while preserving original evidence.
/// </summary>
internal static class ArchitecturalComponentDependencyProjector
{
    public static void Project(ProjectArchitectureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ArchitecturalComponentDependencies.Clear();
        context.ArchitecturalDependencyProjectionDiagnostics.Clear();

        var codeContainerDependencies = context.ArchitectureComponentDependencies
            .Concat(context.ArchitectureComponentInternalDependencies)
            .ToArray();

        if (codeContainerDependencies.Length == 0)
            return;

        var components = context.ArchitecturalComponents
            .Where(component => !string.IsNullOrWhiteSpace(component.Id))
            .GroupBy(component => component.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

        var memberships = context.ArchitecturalComponentMemberships
            .Where(membership => !string.IsNullOrWhiteSpace(membership.CodeComponentId))
            .GroupBy(membership => membership.CodeComponentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1 ? group.Single() : null,
                StringComparer.Ordinal);

        var aggregates = new Dictionary<DependencyKey, DependencyAccumulator>();

        foreach (var dependency in codeContainerDependencies
                     .OrderBy(edge => edge.SourceComponentId, StringComparer.Ordinal)
                     .ThenBy(edge => edge.TargetComponentId, StringComparer.Ordinal)
                     .ThenBy(edge => edge.Scope, StringComparer.Ordinal))
        {
            var hasSourceMembership = memberships.TryGetValue(dependency.SourceComponentId, out var sourceMembership);
            var hasTargetMembership = memberships.TryGetValue(dependency.TargetComponentId, out var targetMembership);

            var sourceArchitecturalId = sourceMembership?.ArchitecturalComponentId;
            var targetArchitecturalId = targetMembership?.ArchitecturalComponentId;

            if (!hasSourceMembership
                || !hasTargetMembership
                || string.IsNullOrWhiteSpace(sourceArchitecturalId)
                || string.IsNullOrWhiteSpace(targetArchitecturalId)
                || !components.ContainsKey(sourceArchitecturalId)
                || !components.ContainsKey(targetArchitecturalId))
            {
                context.ArchitecturalDependencyProjectionDiagnostics.Add(new ArchitecturalDependencyProjectionDiagnostic
                {
                    Code = "ArchitecturalDependencyUnclassifiedEndpoint",
                    Message = "The code-container dependency could not be projected because one or both endpoints have no unique, valid logical architectural membership.",
                    Scope = dependency.Scope,
                    SourceCodeComponentId = dependency.SourceComponentId,
                    TargetCodeComponentId = dependency.TargetComponentId,
                    RelationshipCount = dependency.RelationshipCount,
                    AffectsCoverage = true
                });
                continue;
            }

            var key = new DependencyKey(
                sourceArchitecturalId,
                targetArchitecturalId,
                dependency.Scope);

            if (!aggregates.TryGetValue(key, out var accumulator))
            {
                accumulator = new DependencyAccumulator(
                    components[sourceArchitecturalId],
                    components[targetArchitecturalId]);
                aggregates.Add(key, accumulator);
            }

            accumulator.Add(dependency);
        }

        context.ArchitecturalComponentDependencies.AddRange(
            aggregates
                .Select(pair => pair.Value.ToContext(pair.Key.Scope))
                .OrderBy(edge => edge.SourceComponentName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.TargetComponentName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.Scope, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.SourceComponentId, StringComparer.Ordinal)
                .ThenBy(edge => edge.TargetComponentId, StringComparer.Ordinal));

        context.ArchitecturalDependencyProjectionDiagnostics =
            context.ArchitecturalDependencyProjectionDiagnostics
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.SourceCodeComponentId, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.TargetCodeComponentId, StringComparer.Ordinal)
                .ToList();
    }

    private sealed record DependencyKey(
        string SourceComponentId,
        string TargetComponentId,
        string Scope);

    private sealed class DependencyAccumulator(
        ArchitecturalComponentContext source,
        ArchitecturalComponentContext target)
    {
        private readonly List<ArchitectureComponentDependencyContext> _codeContainerDependencies = [];
        private readonly HashSet<(string RunId, string RelationshipId)> _relationshipReferences = [];
        private readonly HashSet<SemanticRelationshipKind> _relationshipKinds = [];
        private readonly HashSet<string> _evidenceFiles = new(PathComparer);
        private int _relationshipCount;
        private int _resolvedRelationshipCount;
        private int _partiallyResolvedRelationshipCount;
        private int _unreferencedRelationshipCount;

        public void Add(ArchitectureComponentDependencyContext dependency)
        {
            _codeContainerDependencies.Add(dependency);
            _relationshipCount += dependency.RelationshipCount;
            _resolvedRelationshipCount += dependency.ResolvedRelationshipCount;
            _partiallyResolvedRelationshipCount += dependency.PartiallyResolvedRelationshipCount;
            _unreferencedRelationshipCount += dependency.UnreferencedRelationshipCount;

            foreach (var reference in dependency.ContributingRelationships)
                _relationshipReferences.Add((reference.RunId, reference.RelationshipId));

            foreach (var kind in dependency.RelationshipKinds)
                _relationshipKinds.Add(kind);

            foreach (var file in dependency.EvidenceFiles)
                _evidenceFiles.Add(file);
        }

        public ArchitecturalComponentDependencyContext ToContext(string scope) => new()
        {
            SourceComponentId = source.Id,
            SourceComponentName = source.Name,
            TargetComponentId = target.Id,
            TargetComponentName = target.Name,
            Scope = scope,
            IsInternal = string.Equals(source.Id, target.Id, StringComparison.Ordinal),
            RelationshipCount = _relationshipCount,
            ResolvedRelationshipCount = _resolvedRelationshipCount,
            PartiallyResolvedRelationshipCount = _partiallyResolvedRelationshipCount,
            ContributingRelationships = _relationshipReferences
                .OrderBy(reference => reference.RunId, StringComparer.Ordinal)
                .ThenBy(reference => reference.RelationshipId, StringComparer.Ordinal)
                .Select(reference => new SemanticRelationshipReference
                {
                    RunId = reference.RunId,
                    RelationshipId = reference.RelationshipId
                })
                .ToList(),
            UnreferencedRelationshipCount = _unreferencedRelationshipCount,
            RelationshipKinds = _relationshipKinds.OrderBy(kind => kind).ToList(),
            EvidenceFiles = _evidenceFiles.OrderBy(file => file, PathComparer).ToList(),
            ContributingCodeContainerDependencies = _codeContainerDependencies
                .OrderBy(edge => edge.SourceComponentId, StringComparer.Ordinal)
                .ThenBy(edge => edge.TargetComponentId, StringComparer.Ordinal)
                .ThenBy(edge => edge.Scope, StringComparer.Ordinal)
                .ToList()
        };

        private static StringComparer PathComparer =>
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }
}
