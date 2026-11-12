using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// An observed compiler-resolved dependency between language-level architectural
/// components. This graph is independent of build-module dependency edges.
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

    public int RelationshipCount { get; init; }

    public List<SemanticRelationshipKind> RelationshipKinds { get; init; } = [];

    public List<string> EvidenceFiles { get; init; } = [];
}
