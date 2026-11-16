using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Static correlation result for one client-request occurrence. A unique
/// candidate is not proof of runtime execution or successful delivery.
/// </summary>
public sealed record ApiClientRequestEndpointMatch(
    int ClientRequestIndex,
    ApiClientRequestEndpointMatchStatus Status,
    IReadOnlyList<ApiClientRequestEndpointMatchCandidate> Candidates,
    IReadOnlyList<string> Evidence);
