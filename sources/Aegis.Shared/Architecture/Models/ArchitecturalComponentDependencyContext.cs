using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// A semantic dependency between logical architectural components. Dependencies
/// whose source and target are the same component are retained as internal coupling.
/// </summary>
public sealed class ArchitecturalComponentDependencyContext
{
    public string SourceComponentId { get; init; } = string.Empty;

    public string SourceComponentName { get; init; } = string.Empty;

    public string TargetComponentId { get; init; } = string.Empty;

    public string TargetComponentName { get; init; } = string.Empty;

    public string Scope { get; init; } = "Production";

    public bool IsInternal { get; init; }

    public int RelationshipCount { get; init; }

    public int ResolvedRelationshipCount { get; init; }

    public int PartiallyResolvedRelationshipCount { get; init; }

    public List<SemanticRelationshipReference> ContributingRelationships { get; init; } = [];

    public int UnreferencedRelationshipCount { get; init; }

    public List<SemanticRelationshipKind> RelationshipKinds { get; init; } = [];

    public List<string> EvidenceFiles { get; init; } = [];

    /// <summary>
    /// Original language-level edges retained for drill-down and audit. Their
    /// evidence is not reconstructed from aggregate counts.
    /// </summary>
    public List<ArchitectureComponentDependencyContext> ContributingCodeContainerDependencies { get; init; } = [];
}
