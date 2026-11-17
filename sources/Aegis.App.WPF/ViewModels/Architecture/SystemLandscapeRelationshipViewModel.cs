using Aegis.Shared.Architecture.Models;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>Presentation wrapper retaining the original graph edge and its exact evidence references.</summary>
public sealed record SystemLandscapeRelationshipViewModel(
    ArchitectureGraphEdge EvidenceEdge,
    string Source,
    string Target)
{
    public string EdgeId => EvidenceEdge.Id;
    public string Domain => EvidenceEdge.Domain.ToString();
    public string Scope => EvidenceEdge.Scope;
    public string RelationshipKind => string.IsNullOrWhiteSpace(EvidenceEdge.ProviderRelationshipKind)
        ? string.Join(", ", EvidenceEdge.RelationshipKinds)
        : EvidenceEdge.ProviderRelationshipKind;
    public int EvidenceOccurrenceCount => EvidenceEdge.Evidence.Count;
    public bool IsSelfLoop => string.Equals(EvidenceEdge.SourceNodeId, EvidenceEdge.TargetNodeId, StringComparison.Ordinal);
    public IReadOnlyList<ArchitectureGraphEvidenceReference> Evidence => EvidenceEdge.Evidence;
    public string Connection => $"{Source} → {Target}";
    public string EvidenceSummary => $"{EvidenceOccurrenceCount:N0} evidence occurrence(s)";
}
