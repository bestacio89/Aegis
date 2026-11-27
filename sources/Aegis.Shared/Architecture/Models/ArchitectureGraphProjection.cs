namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Immutable deterministic graph snapshot derived from the existing architecture
/// projections and integration registries. Source registries remain authoritative.
/// </summary>
public sealed class ArchitectureGraphProjection
{
    public ArchitectureGraphProjection(
        IEnumerable<ArchitectureGraphNode> nodes,
        IEnumerable<ArchitectureGraphEdge> edges,
        IEnumerable<ArchitectureGraphUnprojectedEvidence> unprojectedEvidence)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(unprojectedEvidence);

        Nodes = Array.AsReadOnly(nodes.ToArray());
        Edges = Array.AsReadOnly(edges.ToArray());
        UnprojectedEvidence = Array.AsReadOnly(unprojectedEvidence.ToArray());
    }

    public IReadOnlyList<ArchitectureGraphNode> Nodes { get; }
    public IReadOnlyList<ArchitectureGraphEdge> Edges { get; }
    public IReadOnlyList<ArchitectureGraphUnprojectedEvidence> UnprojectedEvidence { get; }
}
