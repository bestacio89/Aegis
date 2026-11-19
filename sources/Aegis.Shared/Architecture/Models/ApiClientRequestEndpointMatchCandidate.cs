using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// One endpoint occurrence considered for a client request. Endpoint indexes
/// are scoped to the source ServerApiEndpointDiscoveryRegistry snapshot.
/// </summary>
public sealed record ApiClientRequestEndpointMatchCandidate(
    int EndpointIndex,
    ApiClientRequestEndpointMatchKind MatchKind,
    IReadOnlyList<string> Evidence);
