namespace Aegis.Shared.Architecture.Enums;

/// <summary>Describes the outcome of static client-request to server-endpoint correlation.</summary>
public enum ApiClientRequestEndpointMatchStatus
{
    /// <summary>Exactly one statically compatible endpoint candidate was found.</summary>
    UniqueCandidate,
    /// <summary>Multiple statically compatible endpoint candidates were found.</summary>
    AmbiguousCandidates,
    /// <summary>All relevant evidence was evaluable and no endpoint candidate matched.</summary>
    NoCandidate,
    /// <summary>Available evidence is insufficient to determine whether an endpoint matches.</summary>
    Unresolved,
    /// <summary>The request or endpoint evidence uses a form this matcher does not support.</summary>
    Unsupported
}
