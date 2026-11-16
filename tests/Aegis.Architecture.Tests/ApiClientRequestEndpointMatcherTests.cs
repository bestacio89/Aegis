using Aegis.Architecture.Analysis.Api;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ApiClientRequestEndpointMatcherTests
{
    private readonly ApiClientRequestEndpointMatcher _matcher = new();

    [Fact]
    public void Match_correlates_a_static_request_with_a_route_parameter_candidate()
    {
        var request = CreateRequest(
            httpMethod: "GET",
            requestUriExpression: "\"/api/orders/42?include=items\"");
        var endpoint = CreateEndpoint(
            routeTemplate: "/api/orders/{id}",
            httpMethods: ["GET"]);

        var result = Match([request], [endpoint]).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.UniqueCandidate, result.Status);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(0, candidate.EndpointIndex);
        Assert.Equal(ApiClientRequestEndpointMatchKind.RouteTemplate, candidate.MatchKind);
        Assert.Contains(result.Evidence, item => item.Contains("source-level contract candidate", StringComparison.Ordinal));
    }

    [Fact]
    public void Match_prefers_exact_path_evidence_and_requires_the_http_method()
    {
        var request = CreateRequest(
            httpMethod: "GET",
            requestUriExpression: "\"/api/orders?include=items\"");
        var endpoint = CreateEndpoint(
            routeTemplate: "/api/orders",
            httpMethods: ["GET", "POST"]);

        var result = Match([request], [endpoint]).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.UniqueCandidate, result.Status);
        Assert.Equal(ApiClientRequestEndpointMatchKind.ExactPath, Assert.Single(result.Candidates).MatchKind);

        var mismatchedMethod = Match(
            [CreateRequest(httpMethod: "DELETE", requestUriExpression: "\"/api/orders\"")],
            [endpoint]).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.NoCandidate, mismatchedMethod.Status);
        Assert.Empty(mismatchedMethod.Candidates);
    }

    [Fact]
    public void Match_supports_optional_and_terminal_catch_all_route_segments()
    {
        var optionalParameter = Match(
            [CreateRequest("GET", "\"/api/orders\"")],
            [CreateEndpoint("/api/orders/{id?}", ["GET"])]).Matches.Single();
        var catchAll = Match(
            [CreateRequest("GET", "\"/files/a/b\"")],
            [CreateEndpoint("/files/{*path}", ["GET"])]).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.UniqueCandidate, optionalParameter.Status);
        Assert.Equal(ApiClientRequestEndpointMatchKind.RouteTemplate, Assert.Single(optionalParameter.Candidates).MatchKind);
        Assert.Equal(ApiClientRequestEndpointMatchStatus.UniqueCandidate, catchAll.Status);
        Assert.Equal(ApiClientRequestEndpointMatchKind.RouteTemplate, Assert.Single(catchAll.Candidates).MatchKind);
    }

    [Fact]
    public void Match_does_not_ignore_unmodelled_route_constraints()
    {
        var request = CreateRequest("GET", "\"/orders/not-a-number\"");
        var constrainedAspNetEndpoint = CreateEndpoint("/orders/{id:int}", ["GET"]);
        var constrainedExpressEndpoint = CreateEndpoint("/orders/:id(\\\\d+)", ["GET"]);

        var results = Match(
            [request],
            [constrainedAspNetEndpoint, constrainedExpressEndpoint]).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.Unresolved, results.Status);
        Assert.Equal(2, results.Candidates.Count);
        Assert.All(results.Candidates, candidate =>
            Assert.Equal(ApiClientRequestEndpointMatchKind.UnresolvedEndpoint, candidate.MatchKind));
    }

    [Fact]
    public void Match_uses_conservative_case_sensitive_path_comparison()
    {
        var request = CreateRequest("GET", "\"/api/orders\"");
        var endpoint = CreateEndpoint("/api/Orders", ["GET"]);

        var result = Match([request], [endpoint]).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.NoCandidate, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Match_reports_ambiguous_candidates_instead_of_selecting_a_module()
    {
        var request = CreateRequest(
            httpMethod: "GET",
            requestUriExpression: "\"/api/orders\"");
        var endpoints = new[]
        {
            CreateEndpoint("/api/orders", ["GET"], modulePath: "/repo/orders-api"),
            CreateEndpoint("/api/orders", ["GET"], modulePath: "/repo/catalog-api")
        };

        var result = Match([request], endpoints).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.AmbiguousCandidates, result.Status);
        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, candidate =>
            Assert.Equal(ApiClientRequestEndpointMatchKind.ExactPath, candidate.MatchKind));
    }

    [Fact]
    public void Match_does_not_claim_uniqueness_when_another_compatible_endpoint_is_unresolved()
    {
        var request = CreateRequest(
            httpMethod: "GET",
            requestUriExpression: "\"/api/orders\"");
        var endpoints = new[]
        {
            CreateEndpoint("/api/orders", ["GET"]),
            CreateEndpoint(
                routeTemplate: null,
                httpMethods: ["GET"],
                resolutionStatus: ApiEndpointResolutionStatus.Dynamic)
        };

        var result = Match([request], endpoints).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.Unresolved, result.Status);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(result.Candidates, candidate =>
            candidate.MatchKind == ApiClientRequestEndpointMatchKind.UnresolvedEndpoint);
    }

    [Fact]
    public void Match_preserves_unresolved_request_and_base_address_evidence()
    {
        var dynamicRequest = CreateRequest(
            httpMethod: "GET",
            requestUriExpression: "endpoint",
            resolutionStatus: ApiClientRequestResolutionStatus.Dynamic);
        var baseAddressRequest = CreateRequest(
            httpMethod: "GET",
            requestUriExpression: "\"/api/orders\"",
            baseAddressExpression: "\"https://orders.example.com\"");
        var endpoint = CreateEndpoint("/api/orders", ["GET"]);

        var results = Match([dynamicRequest, baseAddressRequest], [endpoint]).Matches;

        Assert.Equal(2, results.Count);
        Assert.All(results, result =>
            Assert.Equal(ApiClientRequestEndpointMatchStatus.Unresolved, result.Status));
        Assert.All(results, result => Assert.Empty(result.Candidates));
    }

    [Fact]
    public void Match_does_not_root_path_relative_request_uris_without_a_base_address()
    {
        var relativeRequest = CreateRequest(
            httpMethod: "GET",
            requestUriExpression: "\"orders/42\"");
        var rootRelativeRequest = CreateRequest(
            httpMethod: "GET",
            requestUriExpression: "\"/orders/42\"");
        var endpoint = CreateEndpoint("/orders/{id}", ["GET"]);

        var results = Match([relativeRequest, rootRelativeRequest], [endpoint]).Matches;

        Assert.Equal(ApiClientRequestEndpointMatchStatus.Unresolved, results[0].Status);
        Assert.Empty(results[0].Candidates);
        Assert.Equal(ApiClientRequestEndpointMatchStatus.UniqueCandidate, results[1].Status);
        Assert.Equal(ApiClientRequestEndpointMatchKind.RouteTemplate, Assert.Single(results[1].Candidates).MatchKind);
    }

    [Fact]
    public void Match_keeps_query_only_request_targets_unresolved()
    {
        var request = CreateRequest(
            httpMethod: "GET",
            requestUriExpression: "\"?include=items\"");
        var endpoint = CreateEndpoint("/api/orders", ["GET"]);

        var result = Match([request], [endpoint]).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.Unresolved, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Match_does_not_correlate_absolute_uris_without_endpoint_host_identity()
    {
        var request = CreateRequest(
            httpMethod: "GET",
            requestUriExpression: "\"https://orders.example.com/api/orders\"");
        var endpoint = CreateEndpoint("/api/orders", ["GET"]);

        var result = Match([request], [endpoint]).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.Unresolved, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Match_preserves_unsupported_requests_as_explicit_results()
    {
        var request = CreateRequest(
            httpMethod: null,
            requestUriExpression: null,
            resolutionStatus: ApiClientRequestResolutionStatus.Unsupported);

        var result = Match([request], []).Matches.Single();

        Assert.Equal(ApiClientRequestEndpointMatchStatus.Unsupported, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Registry_rejects_multiple_results_for_the_same_request_occurrence()
    {
        var match = new ApiClientRequestEndpointMatch(
            0,
            ApiClientRequestEndpointMatchStatus.NoCandidate,
            [],
            ["No compatible endpoint was found."]);

        Assert.Throws<ArgumentException>(() =>
            new ApiClientRequestEndpointMatchRegistry([match, match]));
    }

    private ApiClientRequestEndpointMatchRegistry Match(
        IEnumerable<ApiClientRequestEvidence> requests,
        IEnumerable<ApiEndpointEvidence> endpoints) =>
        _matcher.Match(
            new ApiClientRequestDiscoveryRegistry(requests),
            new ServerApiEndpointDiscoveryRegistry(endpoints));

    private static ApiClientRequestEvidence CreateRequest(
        string? httpMethod,
        string? requestUriExpression,
        string? baseAddressExpression = null,
        ApiClientRequestResolutionStatus resolutionStatus = ApiClientRequestResolutionStatus.Resolved) =>
        new()
        {
            EvidenceId = "request-evidence",
            Language = "TypeScript",
            Framework = "Fetch API",
            ProviderId = "typescript-fetch",
            ModulePath = "/repo/frontend",
            SourceFile = "/repo/frontend/orders.ts",
            StartLine = 10,
            StartColumn = 4,
            CallerIdentity = "OrdersClient.getOrder",
            HttpMethod = httpMethod,
            RequestUriExpression = requestUriExpression,
            BaseAddressExpression = baseAddressExpression,
            ResolutionStatus = resolutionStatus,
            DeclarationEvidence = "fetch(requestUri)"
        };

    private static ApiEndpointEvidence CreateEndpoint(
        string? routeTemplate,
        IReadOnlyList<string> httpMethods,
        string modulePath = "/repo/backend",
        ApiEndpointResolutionStatus resolutionStatus = ApiEndpointResolutionStatus.Resolved) =>
        new()
        {
            EvidenceId = Guid.NewGuid().ToString("N"),
            Language = "C#",
            Framework = "ASP.NET Core",
            ProviderId = "roslyn-aspnet",
            ModulePath = modulePath,
            SourceFile = Path.Combine(modulePath, "OrdersController.cs"),
            StartLine = 20,
            StartColumn = 8,
            HandlerIdentity = "OrdersController.GetOrder",
            HttpMethods = httpMethods,
            RouteTemplate = routeTemplate,
            ResolutionStatus = resolutionStatus,
            DeclarationEvidence = "MapGet(\"/api/orders\", handler)"
        };
}
