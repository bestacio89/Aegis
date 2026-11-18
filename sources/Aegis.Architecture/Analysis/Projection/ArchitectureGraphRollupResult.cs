using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Projection;

/// <summary>Immutable package-level view over an unchanged leaf graph projection.</summary>
public sealed class ArchitectureGraphRollupResult
{
    public ArchitectureGraphRollupResult(
        string basePackage,
        int depth,
        IEnumerable<ArchitectureGraphRollupNode> nodes,
        IEnumerable<ArchitectureGraphRollupEdge> edges,
        IEnumerable<ArchitectureGraphEdge> unresolvedEdges,
        IEnumerable<ArchitectureGraphUnprojectedEvidence> unprojectedEvidence)
    {
        BasePackage = basePackage;
        Depth = depth;
        Nodes = Array.AsReadOnly(nodes.ToArray());
        Edges = Array.AsReadOnly(edges.ToArray());
        UnresolvedEdges = Array.AsReadOnly(unresolvedEdges.ToArray());
        UnprojectedEvidence = Array.AsReadOnly(unprojectedEvidence.ToArray());
    }

    public string BasePackage { get; }
    public int Depth { get; }
    public IReadOnlyList<ArchitectureGraphRollupNode> Nodes { get; }
    public IReadOnlyList<ArchitectureGraphRollupEdge> Edges { get; }
    public IReadOnlyList<ArchitectureGraphEdge> UnresolvedEdges { get; }
    public IReadOnlyList<ArchitectureGraphUnprojectedEvidence> UnprojectedEvidence { get; }
}
