using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>An evidence occurrence deliberately retained outside the graph edge set.</summary>
public sealed record ArchitectureGraphUnprojectedEvidence
{
    public ArchitectureGraphEvidenceReference Evidence { get; init; } = new();
    public ArchitectureGraphUnprojectedReason Reason { get; init; }
    public string Explanation { get; init; } = string.Empty;
}
