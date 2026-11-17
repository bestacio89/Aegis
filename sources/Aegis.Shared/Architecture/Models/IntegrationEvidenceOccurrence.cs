using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// A stable reference to one occurrence in a domain registry snapshot.
/// The evidence object remains the authoritative domain-specific contract.
/// </summary>
public sealed record IntegrationEvidenceOccurrence(
    IntegrationEvidenceDomain Domain,
    int OccurrenceIndex,
    IIntegrationEvidence Evidence);
