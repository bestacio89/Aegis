using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Projection;

/// <summary>An aggregated edge retaining contributing leaf-edge and evidence occurrences.</summary>
public sealed record ArchitectureGraphRollupEdge
{
    public string SourceGroupKey { get; init; } = string.Empty;
    public string TargetGroupKey { get; init; } = string.Empty;
    public ArchitectureGraphEdgeDomain Domain { get; init; }
    public string Scope { get; init; } = "Production";
    public int GroupDepth { get; init; }
    public int RelationshipCount { get; init; }
    public IReadOnlyList<ArchitectureRelationshipKind> RelationshipKinds { get; init; } = Array.Empty<ArchitectureRelationshipKind>();
    public IReadOnlyList<string> ProviderRelationshipKinds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ArchitectureGraphEvidenceReference> Evidence { get; init; } = Array.Empty<ArchitectureGraphEvidenceReference>();
    public IReadOnlyList<string> LeafEdgeIds { get; init; } = Array.Empty<string>();
}
