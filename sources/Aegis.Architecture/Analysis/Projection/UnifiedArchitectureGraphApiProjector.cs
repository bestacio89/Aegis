using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Projection;

/// <summary>
/// Projects statically matched HTTP request and endpoint occurrences into the unified graph.
/// The source registries remain authoritative; a match is not runtime reachability evidence.
/// </summary>
internal static partial class UnifiedArchitectureGraphProjector
{
    private static void ProjectApiEvidence(
        ProjectArchitectureContext context,
        IEnumerable<ProjectModuleContext> modules,
        IDictionary<string, ArchitectureGraphNode> nodes,
        ICollection<ArchitectureGraphEdge> edges,
        ICollection<ArchitectureGraphUnprojectedEvidence> unprojected)
    {
        var requests = context.ApiClientRequestDiscoveryRegistry?.Evidence
            ?? Array.Empty<ApiClientRequestEvidence>();
        var endpoints = context.ServerApiEndpointDiscoveryRegistry?.Evidence
            ?? Array.Empty<ApiEndpointEvidence>();
        var matches = context.ApiClientRequestEndpointMatchRegistry?.Matches;

        if (matches is null)
        {
            for (var requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                AddUnprojectedApiRequest(
                    requestIndex,
                    requests[requestIndex],
                    ArchitectureGraphUnprojectedReason.UnresolvedEvidence,
                    "No API request-to-endpoint match snapshot is available.");
            }

            return;
        }

        foreach (var match in matches.OrderBy(item => item.ClientRequestIndex))
        {
            if (match.ClientRequestIndex < 0 || match.ClientRequestIndex >= requests.Count)
            {
                unprojected.Add(new ArchitectureGraphUnprojectedEvidence
                {
                    Evidence = new ArchitectureGraphEvidenceReference
                    {
                        Domain = ArchitectureGraphEvidenceDomain.ApiClientRequest,
                        OccurrenceIndex = match.ClientRequestIndex,
                        EvidenceId = $"request-index:{match.ClientRequestIndex}"
                    },
                    Reason = ArchitectureGraphUnprojectedReason.InvalidApiEvidenceReference,
                    Explanation = "The match references a client-request occurrence outside the authoritative request registry snapshot."
                });
                continue;
            }

            var request = requests[match.ClientRequestIndex];
            if (match.Status != ApiClientRequestEndpointMatchStatus.UniqueCandidate)
            {
                var reason = match.Status switch
                {
                    ApiClientRequestEndpointMatchStatus.NoCandidate =>
                        ArchitectureGraphUnprojectedReason.NoApiCandidate,
                    ApiClientRequestEndpointMatchStatus.AmbiguousCandidates =>
                        ArchitectureGraphUnprojectedReason.AmbiguousApiMatch,
                    ApiClientRequestEndpointMatchStatus.Unsupported =>
                        ArchitectureGraphUnprojectedReason.UnsupportedApiMatch,
                    _ => ArchitectureGraphUnprojectedReason.UnresolvedEvidence
                };

                AddUnprojectedApiRequest(
                    match.ClientRequestIndex,
                    request,
                    reason,
                    $"API request matching completed with status {match.Status}; only a unique supported candidate is eligible for projection.");
                continue;
            }

            if (match.Candidates.Count != 1)
            {
                AddUnprojectedApiRequest(
                    match.ClientRequestIndex,
                    request,
                    ArchitectureGraphUnprojectedReason.AmbiguousApiMatch,
                    "A unique match status did not contain exactly one endpoint candidate.");
                continue;
            }

            var candidate = match.Candidates[0];
            if (candidate.MatchKind == ApiClientRequestEndpointMatchKind.UnresolvedEndpoint)
            {
                AddUnprojectedApiRequest(
                    match.ClientRequestIndex,
                    request,
                    ArchitectureGraphUnprojectedReason.UnresolvedEvidence,
                    "The sole candidate represents an unresolved endpoint rather than a supported path match.");
                continue;
            }

            if (candidate.MatchKind is not (
                ApiClientRequestEndpointMatchKind.ExactPath
                or ApiClientRequestEndpointMatchKind.RouteTemplate))
            {
                AddUnprojectedApiRequest(
                    match.ClientRequestIndex,
                    request,
                    ArchitectureGraphUnprojectedReason.UnsupportedApiMatch,
                    $"The API match kind {candidate.MatchKind} is not supported for graph projection.");
                continue;
            }

            if (candidate.EndpointIndex < 0 || candidate.EndpointIndex >= endpoints.Count)
            {
                AddUnprojectedApiRequest(
                    match.ClientRequestIndex,
                    request,
                    ArchitectureGraphUnprojectedReason.InvalidApiEvidenceReference,
                    "The match references an endpoint occurrence outside the authoritative endpoint registry snapshot.");
                continue;
            }

            var endpoint = endpoints[candidate.EndpointIndex];
            if (request.ResolutionStatus != ApiClientRequestResolutionStatus.Resolved
                || endpoint.ResolutionStatus != ApiEndpointResolutionStatus.Resolved)
            {
                AddUnprojectedApiRequest(
                    match.ClientRequestIndex,
                    request,
                    ArchitectureGraphUnprojectedReason.UnresolvedEvidence,
                    $"Both request and endpoint evidence must be fully resolved; observed request={request.ResolutionStatus}, endpoint={endpoint.ResolutionStatus}.");
                continue;
            }

            if (!TryResolveModule(request.ModulePath, modules, out var sourceModule, out var sourceAmbiguous))
            {
                AddUnprojectedApiRequest(
                    match.ClientRequestIndex,
                    request,
                    sourceAmbiguous
                        ? ArchitectureGraphUnprojectedReason.AmbiguousEndpointOwnership
                        : ArchitectureGraphUnprojectedReason.MissingEndpoint,
                    "The client request is not owned by exactly one discovered build module.");
                continue;
            }

            if (!TryResolveModule(endpoint.ModulePath, modules, out var targetModule, out var targetAmbiguous))
            {
                AddUnprojectedApiRequest(
                    match.ClientRequestIndex,
                    request,
                    targetAmbiguous
                        ? ArchitectureGraphUnprojectedReason.AmbiguousEndpointOwnership
                        : ArchitectureGraphUnprojectedReason.MissingEndpoint,
                    "The server endpoint is not owned by exactly one discovered build module.");
                continue;
            }

            var sourceId = CreateModuleNodeId(sourceModule.Path);
            var targetId = CreateModuleNodeId(targetModule.Path);
            if (!nodes.ContainsKey(sourceId) || !nodes.ContainsKey(targetId))
            {
                AddUnprojectedApiRequest(
                    match.ClientRequestIndex,
                    request,
                    ArchitectureGraphUnprojectedReason.MissingEndpoint,
                    "One or both API endpoint modules are absent from the unified graph module-node snapshot.");
                continue;
            }

            var scope = NormalizeScope("Production", sourceModule, targetModule);
            var requestReference = new ArchitectureGraphEvidenceReference
            {
                Domain = ArchitectureGraphEvidenceDomain.ApiClientRequest,
                OccurrenceIndex = match.ClientRequestIndex,
                EvidenceId = request.EvidenceId,
                ProviderId = request.ProviderId
            };
            var endpointReference = new ArchitectureGraphEvidenceReference
            {
                Domain = ArchitectureGraphEvidenceDomain.ApiEndpoint,
                OccurrenceIndex = candidate.EndpointIndex,
                EvidenceId = endpoint.EvidenceId,
                ProviderId = endpoint.ProviderId
            };

            edges.Add(new ArchitectureGraphEdge
            {
                Id = CreateEdgeId(
                    "api",
                    sourceId,
                    targetId,
                    scope,
                    $"{match.ClientRequestIndex}:{candidate.EndpointIndex}"),
                SourceNodeId = sourceId,
                TargetNodeId = targetId,
                Domain = ArchitectureGraphEdgeDomain.Api,
                Scope = scope,
                ProviderRelationshipKind = "StaticApiRequestMatch",
                RelationshipKinds = Array.AsReadOnly(
                    new[] { ArchitectureRelationshipKind.HttpRequest }),
                Evidence = Array.AsReadOnly(
                    new[] { requestReference, endpointReference })
            });
        }

        var representedRequestIndexes = matches
            .Where(match => match.ClientRequestIndex >= 0 && match.ClientRequestIndex < requests.Count)
            .Select(match => match.ClientRequestIndex)
            .ToHashSet();

        for (var requestIndex = 0; requestIndex < requests.Count; requestIndex++)
        {
            if (representedRequestIndexes.Contains(requestIndex))
                continue;

            AddUnprojectedApiRequest(
                requestIndex,
                requests[requestIndex],
                ArchitectureGraphUnprojectedReason.UnresolvedEvidence,
                "The authoritative match snapshot contains no result for this client-request occurrence.");
        }

        void AddUnprojectedApiRequest(
            int requestIndex,
            ApiClientRequestEvidence request,
            ArchitectureGraphUnprojectedReason reason,
            string explanation)
        {
            unprojected.Add(new ArchitectureGraphUnprojectedEvidence
            {
                Evidence = new ArchitectureGraphEvidenceReference
                {
                    Domain = ArchitectureGraphEvidenceDomain.ApiClientRequest,
                    OccurrenceIndex = requestIndex,
                    EvidenceId = request.EvidenceId,
                    ProviderId = request.ProviderId
                },
                Reason = reason,
                Explanation = explanation
            });
        }
    }
}
