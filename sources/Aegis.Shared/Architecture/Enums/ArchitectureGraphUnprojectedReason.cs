namespace Aegis.Shared.Architecture.Enums;

/// <summary>Explains why an evidence occurrence was retained without producing a graph edge.</summary>
public enum ArchitectureGraphUnprojectedReason
{
    MissingEndpoint = 1,
    AmbiguousEndpointOwnership = 2,
    MissingResourceIdentity = 3,
    UnresolvedEvidence = 4,
    AmbiguousEvidenceReference = 5,
    UnsupportedRelationship = 6
}
