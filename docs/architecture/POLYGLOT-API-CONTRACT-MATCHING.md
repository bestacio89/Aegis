# Polyglot API contract matching and endpoint resolution

## Purpose

Phase 9 correlates source-level outbound HTTP request observations with server endpoint declarations. A match is a static contract candidate only. It does not prove that a request executes, reaches the endpoint at runtime, passes authentication, or succeeds.

The matcher consumes the Phase 7 server endpoint registry and Phase 8 client request registry. It does not rerun providers, mutate source evidence, or create graph edges.

## Canonical inputs and occurrence identity

- Client request occurrences are identified by their zero-based index in the current `ApiClientRequestDiscoveryRegistry` snapshot.
- Server endpoint occurrences are identified by their zero-based index in the current `ServerApiEndpointDiscoveryRegistry` snapshot.
- Provider `EvidenceId` values are retained in the source registries but are not assumed globally unique.
- Candidate references use snapshot indexes so duplicate provider IDs and duplicate route declarations remain distinguishable.
- Match results are derived and excluded from report serialization; the source registries remain authoritative.

## Matching rules

A request is eligible for route matching only when:
- its provider resolution status is `Resolved`;
- its HTTP method is statically known;
- its request URI expression can be read as a supported string literal;
- its URI is relative and has no unmodelled base-address expression.

A server endpoint is eligible for a unique candidate only when:
- its route status is `Resolved`;
- its route template is available;
- its HTTP method set is explicit.

A candidate requires both an HTTP-method match and a route-path match:
- HTTP methods are compared case-insensitively.
- Paths are compared ordinally and case-sensitively.
- Query strings and fragments are excluded from request-path comparison.
- Exact paths produce `ExactPath` candidates.
- Segment-based route parameters such as `{id}` and `:id` produce `RouteTemplate` candidates.
- Optional whole-segment parameters and terminal catch-all segments are supported.
- Route constraints, parameter defaults, embedded parameter expressions, custom parameter regexes, route rewriting, percent-decoding, and framework-specific case/trailing-slash configuration are not inferred.

The matcher does not treat a matching method alone, a similar route string, a DTO shape, a build dependency, or a shared host name as proof of a match.

## Result statuses

| Status | Meaning |
|---|---|
| `UniqueCandidate` | Exactly one resolved endpoint matches the static method and path, with no unresolved compatible endpoint evidence |
| `AmbiguousCandidates` | More than one resolved endpoint matches; no endpoint is selected |
| `NoCandidate` | No endpoint in the available, evaluable evidence matches both method and path |
| `Unresolved` | Request evidence, base-address composition, host identity, or a potentially compatible endpoint remains unresolved |
| `Unsupported` | The client provider explicitly marked the request form unsupported |

A `NoCandidate` result does not prove that no external endpoint exists. It means only that no candidate was found in the current endpoint evidence snapshot.

Unresolved endpoint declarations with a compatible or unknown method are retained as unresolved candidates. They prevent the matcher from claiming a unique candidate when their route could still match.

## Deliberate limitations

- Absolute request URIs remain unresolved because server endpoint evidence does not yet carry a host/base-address identity that can safely bind the URI to a module.
- A non-empty base-address expression remains unresolved; application configuration and URI composition are not evaluated.\n- Path-relative references such as `orders/42` and query-only references remain unresolved because their effective path depends on a base URI. They are never rewritten as root-relative paths.
- Dynamic and partially resolved request expressions remain unresolved.
- Unresolved, dynamic, unsupported, or method-less server declarations are not promoted to confirmed candidates.
- Matching does not create `HttpRequest` graph edges. A later projection phase consumes this derived evidence without changing the raw client or server registries.
- Runtime configuration, service discovery, gateway rewrites, proxy routing, authentication, and actual network traffic are outside this source-analysis boundary.

## Validation matrix

- Exact method and exact path.
- Route-template parameters, optional segments, and terminal catch-all routes.
- Query/fragment removal without path rewriting.
- Method mismatch and path mismatch.
- Multiple matching endpoints remain ambiguous.
- An unresolved potentially compatible endpoint prevents a unique result.
- Dynamic, unsupported, absolute-URI, and base-address requests remain explicit.
- Duplicate evidence IDs remain distinguishable through registry occurrence indexes.
- Pipeline integration produces a derived match registry after server and client evidence aggregation.
