using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Snapshot-local reference to one occurrence in an authoritative evidence registry.
/// Evidence IDs are descriptive only; domain and occurrence index establish identity.
/// </summary>
public sealed record ArchitectureGraphEvidenceReference
{
    public ArchitectureGraphEvidenceDomain Domain { get; init; }
    public int OccurrenceIndex { get; init; }
    public string EvidenceId { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
}
