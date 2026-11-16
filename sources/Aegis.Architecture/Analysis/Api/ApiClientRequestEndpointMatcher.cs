using System.Text.Json;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Api;

/// <summary>
/// Correlates statically known HTTP request paths and methods with server route
/// declarations. Results are contract candidates, not proof of runtime traffic.
/// </summary>
public sealed class ApiClientRequestEndpointMatcher
{
    public ApiClientRequestEndpointMatchRegistry Match(
        ApiClientRequestDiscoveryRegistry clientRequests,
        ServerApiEndpointDiscoveryRegistry serverEndpoints)
    {
        ArgumentNullException.ThrowIfNull(clientRequests);
        ArgumentNullException.ThrowIfNull(serverEndpoints);

        var matches = new ApiClientRequestEndpointMatch[clientRequests.Count];
        for (var requestIndex = 0; requestIndex < clientRequests.Count; requestIndex++)
        {
            matches[requestIndex] = MatchRequest(
                requestIndex,
                clientRequests.Evidence[requestIndex],
                serverEndpoints.Evidence);
        }

        return new ApiClientRequestEndpointMatchRegistry(matches);
    }

    private static ApiClientRequestEndpointMatch MatchRequest(
        int requestIndex,
        ApiClientRequestEvidence request,
        IReadOnlyList<ApiEndpointEvidence> endpoints)
    {
        if (request.ResolutionStatus == ApiClientRequestResolutionStatus.Unsupported)
        {
            return CreateResult(
                requestIndex,
                ApiClientRequestEndpointMatchStatus.Unsupported,
                [],
                "The client provider marked this request form as unsupported.");
        }

        if (request.ResolutionStatus != ApiClientRequestResolutionStatus.Resolved)
        {
            return CreateResult(
                requestIndex,
                ApiClientRequestEndpointMatchStatus.Unresolved,
                [],
                "The request URI is dynamic or partially resolved; endpoint matching was not attempted.");
        }

        if (string.IsNullOrWhiteSpace(request.HttpMethod)
            || string.IsNullOrWhiteSpace(request.RequestUriExpression))
        {
            return CreateResult(
                requestIndex,
                ApiClientRequestEndpointMatchStatus.Unresolved,
                [],
                "A statically resolved HTTP method and request URI are both required.");
        }

        if (!TryReadStringLiteral(request.RequestUriExpression, out var requestUri))
        {
            return CreateResult(
                requestIndex,
                ApiClientRequestEndpointMatchStatus.Unresolved,
                [],
                "The provider marked the URI as resolved, but its source expression is not a supported string literal.");
        }

        if (!string.IsNullOrWhiteSpace(request.BaseAddressExpression))
        {
            return CreateResult(
                requestIndex,
                ApiClientRequestEndpointMatchStatus.Unresolved,
                [],
                "A base-address expression is present. Destination host and base-path composition are not evaluated by this matcher.");
        }

        if (!TryGetRelativePath(requestUri, out var requestPath))
        {
            return CreateResult(
                requestIndex,
                ApiClientRequestEndpointMatchStatus.Unresolved,
                [],
                "An absolute or authority-relative URI cannot be associated with a server module because endpoint host identity is not yet modelled.");
        }

        var method = request.HttpMethod.Trim().ToUpperInvariant();
        var candidates = new List<ApiClientRequestEndpointMatchCandidate>();
        var unresolvedEndpointCount = 0;

        for (var endpointIndex = 0; endpointIndex < endpoints.Count; endpointIndex++)
        {
            var endpoint = endpoints[endpointIndex];
            var methodIsKnown = endpoint.HttpMethods.Count > 0;
            var methodMatches = endpoint.HttpMethods.Any(candidate =>
                string.Equals(candidate, method, StringComparison.OrdinalIgnoreCase));
            if (methodIsKnown && !methodMatches)
                continue;

            if (endpoint.ResolutionStatus != ApiEndpointResolutionStatus.Resolved
                || string.IsNullOrWhiteSpace(endpoint.RouteTemplate)
                || !methodIsKnown)
            {
                unresolvedEndpointCount++;
                candidates.Add(new ApiClientRequestEndpointMatchCandidate(
                    endpointIndex,
                    ApiClientRequestEndpointMatchKind.UnresolvedEndpoint,
                    Array.AsReadOnly(new[]
                    {
                        "The endpoint has an unresolved route or method and may still be compatible with this request."
                    })));
                continue;
            }

            var route = NormalizeRoute(endpoint.RouteTemplate);
            if (string.Equals(requestPath, route, StringComparison.Ordinal))
            {
                candidates.Add(new ApiClientRequestEndpointMatchCandidate(
                    endpointIndex,
                    ApiClientRequestEndpointMatchKind.ExactPath,
                    Array.AsReadOnly(new[]
                    {
                        $"HTTP method '{method}' matches.",
                        "The statically known request path exactly matches the resolved endpoint route."
                    })));
                continue;
            }

            if (RouteTemplateMatches(route, requestPath))
            {
                candidates.Add(new ApiClientRequestEndpointMatchCandidate(
                    endpointIndex,
                    ApiClientRequestEndpointMatchKind.RouteTemplate,
                    Array.AsReadOnly(new[]
                    {
                        $"HTTP method '{method}' matches.",
                        "The request path matches the endpoint's segment-based route template."
                    })));
            }
        }

        var resolvedCandidates = candidates.Count(candidate =>
            candidate.MatchKind != ApiClientRequestEndpointMatchKind.UnresolvedEndpoint);

        if (resolvedCandidates > 1)
        {
            return new ApiClientRequestEndpointMatch(
                requestIndex,
                ApiClientRequestEndpointMatchStatus.AmbiguousCandidates,
                Array.AsReadOnly(candidates.ToArray()),
                Array.AsReadOnly(new[]
                {
                    "More than one resolved endpoint has a compatible HTTP method and route. No candidate was selected."
                }));
        }

        if (unresolvedEndpointCount > 0)
        {
            return new ApiClientRequestEndpointMatch(
                requestIndex,
                ApiClientRequestEndpointMatchStatus.Unresolved,
                Array.AsReadOnly(candidates.ToArray()),
                Array.AsReadOnly(new[]
                {
                    "One or more endpoints with a compatible or unknown HTTP method have unresolved route evidence; a unique match cannot be established."
                }));
        }

        if (resolvedCandidates == 1)
        {
            return new ApiClientRequestEndpointMatch(
                requestIndex,
                ApiClientRequestEndpointMatchStatus.UniqueCandidate,
                Array.AsReadOnly(candidates.ToArray()),
                Array.AsReadOnly(new[]
                {
                    "Exactly one resolved endpoint matches the static HTTP method and route. This is a source-level contract candidate, not proof of runtime invocation or delivery."
                }));
        }

        return CreateResult(
            requestIndex,
            ApiClientRequestEndpointMatchStatus.NoCandidate,
            [],
            "All available endpoint evidence was evaluable; no endpoint matched both the HTTP method and route.");
    }

    private static bool TryReadStringLiteral(string expression, out string value)
    {
        value = string.Empty;
        if (expression.Length < 2)
            return false;

        if (expression[0] == '"' && expression[^1] == '"')
        {
            try
            {
                value = JsonSerializer.Deserialize<string>(expression) ?? string.Empty;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        if (expression[0] != '\'' || expression[^1] != '\'')
            return false;

        var builder = new System.Text.StringBuilder(expression.Length - 2);
        for (var index = 1; index < expression.Length - 1; index++)
        {
            var character = expression[index];
            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            if (++index >= expression.Length - 1)
                return false;

            var escaped = expression[index];
            if (escaped is not ('\\' or '\'' or '"' or '/'))
                return false;
            builder.Append(escaped);
        }

        value = builder.ToString();
        return true;
    }

    private static bool TryGetRelativePath(string uriValue, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(uriValue)
            || uriValue.StartsWith("//", StringComparison.Ordinal)
            || HasUriScheme(uriValue))
        {
            return false;
        }

        var queryIndex = uriValue.IndexOfAny(['?', '#']);
        var pathValue = queryIndex >= 0 ? uriValue[..queryIndex] : uriValue;
        if (string.IsNullOrWhiteSpace(pathValue))
            return false;

        path = NormalizeRoute(pathValue);
        return true;
    }

    private static bool HasUriScheme(string value)
    {
        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || !char.IsAsciiLetter(value[0]))
            return false;

        for (var index = 1; index < separatorIndex; index++)
        {
            var character = value[index];
            if (!char.IsAsciiLetterOrDigit(character) && character is not '+' and not '-' and not '.')
                return false;
        }

        return true;
    }

    private static string NormalizeRoute(string route)
    {
        var normalized = route.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;
        return normalized;
    }

    private static bool RouteTemplateMatches(string routeTemplate, string requestPath)
    {
        var routeSegments = routeTemplate.Split('/');
        var requestSegments = requestPath.Split('/');
        return MatchSegments(routeSegments, 1, requestSegments, 1);
    }

    private static bool MatchSegments(
        IReadOnlyList<string> routeSegments,
        int routeIndex,
        IReadOnlyList<string> requestSegments,
        int requestIndex)
    {
        if (routeIndex == routeSegments.Count)
            return requestIndex == requestSegments.Count;

        var routeSegment = routeSegments[routeIndex];
        if (IsCatchAllSegment(routeSegment))
            return routeIndex == routeSegments.Count - 1
                && requestIndex < requestSegments.Count;

        if (IsOptionalParameterSegment(routeSegment))
        {
            if (MatchSegments(routeSegments, routeIndex + 1, requestSegments, requestIndex))
                return true;
        }

        if (requestIndex >= requestSegments.Count)
            return false;

        if (IsParameterSegment(routeSegment))
        {
            if (requestSegments[requestIndex].Length == 0)
                return false;
        }
        else if (!string.Equals(routeSegment, requestSegments[requestIndex], StringComparison.Ordinal))
        {
            return false;
        }

        return MatchSegments(routeSegments, routeIndex + 1, requestSegments, requestIndex + 1);
    }

    private static bool IsParameterSegment(string segment) =>
        segment.Length >= 3
        && segment[0] == '{'
        && segment[^1] == '}'
        || segment.Length > 1 && segment[0] == ':';

    private static bool IsOptionalParameterSegment(string segment) =>
        segment.Length >= 4
        && segment[0] == '{'
        && segment[^2] == '?'
        && segment[^1] == '}';

    private static bool IsCatchAllSegment(string segment) =>
        segment == "*"
        || segment.StartsWith("{*", StringComparison.Ordinal)
        || segment.StartsWith("{**", StringComparison.Ordinal);

    private static ApiClientRequestEndpointMatch CreateResult(
        int requestIndex,
        ApiClientRequestEndpointMatchStatus status,
        IReadOnlyList<ApiClientRequestEndpointMatchCandidate> candidates,
        string evidence) =>
        new(
            requestIndex,
            status,
            candidates,
            Array.AsReadOnly(new[] { evidence }));
}
