using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// An observed semantic dependency between language-level architectural
/// components. Provider resolution provenance is retained in the evidence graph,
/// independently from build-module dependency edges.
/// </summary>
public sealed class ArchitectureComponentDependencyContext
{
    public string SourceComponentId { get; init; } = string.Empty;

    public string SourceComponentName { get; init; } = string.Empty;

    public string TargetComponentId { get; init; } = string.Empty;

    public string TargetComponentName { get; init; } = string.Empty;

    /// <summary>Production or Test; test evidence never enters the production graph.</summary>
    public string Scope { get; init; } = "Production";

    public string DependencyType { get; init; } = "SemanticComponentDependency";

    /// <summary>True when both ends refer to the same language-level code container.</summary>
    public bool IsInternal { get; init; }


    public int RelationshipCount { get; init; }

    /// <summary>Relationships resolved completely by the semantic provider.</summary>
    public int ResolvedRelationshipCount { get; init; }

    /// <summary>Relationships with a usable target but incomplete resolution evidence.</summary>
    public int PartiallyResolvedRelationshipCount { get; init; }

    /// <summary>
    /// Exact source evidence references. A relationship ID is scoped to its
    /// semantic run, so both values are required to resolve the original record.
    /// </summary>
    public List<SemanticRelationshipReference> ContributingRelationships { get; init; } = [];

    /// <summary>Relationships whose provider omitted an ID and therefore cannot be referenced exactly.</summary>
    public int UnreferencedRelationshipCount { get; init; }

    public List<SemanticRelationshipKind> RelationshipKinds { get; init; } = [];

    public List<string> EvidenceFiles { get; init; } = [];
}
