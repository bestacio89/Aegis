using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>A directed relationship in the unified graph, backed by exact evidence occurrences.</summary>
public sealed record ArchitectureGraphEdge
{
    public string Id { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public ArchitectureGraphEdgeDomain Domain { get; init; }
    public string Scope { get; init; } = "Production";
    public string ProviderRelationshipKind { get; init; } = string.Empty;
    public IReadOnlyList<ArchitectureRelationshipKind> RelationshipKinds { get; init; } = Array.Empty<ArchitectureRelationshipKind>();
    public IReadOnlyList<string> AndroidVariants { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ArchitectureGraphEvidenceReference> Evidence { get; init; } = Array.Empty<ArchitectureGraphEvidenceReference>();
}
