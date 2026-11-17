using Aegis.Architecture.Analysis.Projection;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using FluentAssertions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ApiGraphProjectionTests
{
    [Theory]
    [InlineData(ApiClientRequestEndpointMatchKind.ExactPath)]
    [InlineData(ApiClientRequestEndpointMatchKind.RouteTemplate)]
    public void Project_creates_client_to_server_http_edge_for_supported_unique_match(
        ApiClientRequestEndpointMatchKind matchKind)
    {
        var context = CreateContext(
            [CreateRequest("/repo/client", "request-1")],
            [CreateEndpoint("/repo/server", "endpoint-1")],
            [new ApiClientRequestEndpointMatch(
                0,
                ApiClientRequestEndpointMatchStatus.UniqueCandidate,
                [new ApiClientRequestEndpointMatchCandidate(0, matchKind, ["static path match"])],
                ["unique"])],
            [
                CreateModule("client", "/repo/client"),
                CreateModule("server", "/repo/server")
            ]);

        var projection = UnifiedArchitectureGraphProjector.Project(context);

        var edge = projection.Edges.Should().ContainSingle().Subject;
        edge.Domain.Should().Be(ArchitectureGraphEdgeDomain.Api);
        edge.SourceNodeId.Should().Be(ModuleNodeId("/repo/client"));
        edge.TargetNodeId.Should().Be(ModuleNodeId("/repo/server"));
        edge.RelationshipKinds.Should().ContainSingle().Which.Should().Be(ArchitectureRelationshipKind.HttpRequest);
        edge.Scope.Should().Be("Production");
        edge.Evidence.Should().HaveCount(2);
        edge.Evidence.Should().Contain(reference =>
            reference.Domain == ArchitectureGraphEvidenceDomain.ApiClientRequest
            && reference.OccurrenceIndex == 0
            && reference.EvidenceId == "request-1");
        edge.Evidence.Should().Contain(reference =>
            reference.Domain == ArchitectureGraphEvidenceDomain.ApiEndpoint
            && reference.OccurrenceIndex == 0
            && reference.EvidenceId == "endpoint-1");
        projection.UnprojectedEvidence.Should().BeEmpty();
    }

    [Fact]
    public void Project_retains_non_unique_and_unsupported_match_outcomes_without_edges()
    {
        var context = CreateContext(
            [
                CreateRequest("/repo/client", "ambiguous"),
                CreateRequest("/repo/client", "none"),
                CreateRequest("/repo/client", "unsupported"),
                CreateRequest("/repo/client", "unresolved")
            ],
            [CreateEndpoint("/repo/server", "endpoint")],
            [
                new ApiClientRequestEndpointMatch(0, ApiClientRequestEndpointMatchStatus.AmbiguousCandidates, [], []),
                new ApiClientRequestEndpointMatch(1, ApiClientRequestEndpointMatchStatus.NoCandidate, [], []),
                new ApiClientRequestEndpointMatch(2, ApiClientRequestEndpointMatchStatus.Unsupported, [], []),
                new ApiClientRequestEndpointMatch(3, ApiClientRequestEndpointMatchStatus.Unresolved, [], [])
            ],
            [CreateModule("client", "/repo/client"), CreateModule("server", "/repo/server")]);

        var projection = UnifiedArchitectureGraphProjector.Project(context);

        projection.Edges.Should().BeEmpty();
        projection.UnprojectedEvidence.Should().HaveCount(4);
        projection.UnprojectedEvidence.Should().Contain(item =>
            item.Evidence.EvidenceId == "ambiguous"
            && item.Reason == ArchitectureGraphUnprojectedReason.AmbiguousApiMatch);
        projection.UnprojectedEvidence.Should().Contain(item =>
            item.Evidence.EvidenceId == "none"
            && item.Reason == ArchitectureGraphUnprojectedReason.NoApiCandidate);
        projection.UnprojectedEvidence.Should().Contain(item =>
            item.Evidence.EvidenceId == "unsupported"
            && item.Reason == ArchitectureGraphUnprojectedReason.UnsupportedApiMatch);
        projection.UnprojectedEvidence.Should().Contain(item =>
            item.Evidence.EvidenceId == "unresolved"
            && item.Reason == ArchitectureGraphUnprojectedReason.UnresolvedEvidence);
    }

    [Fact]
    public void Project_rejects_unresolved_request_or_endpoint_and_unresolved_candidate_kind()
    {
        var partialRequest = CreateRequest("/repo/client", "partial-request") with
        {
            ResolutionStatus = ApiClientRequestResolutionStatus.PartiallyResolved
        };
        var dynamicEndpoint = CreateEndpoint("/repo/server", "dynamic-endpoint") with
        {
            ResolutionStatus = ApiEndpointResolutionStatus.Dynamic
        };
        var context = CreateContext(
            [
                partialRequest,
                CreateRequest("/repo/client", "dynamic-endpoint-request"),
                CreateRequest("/repo/client", "unresolved-candidate")
            ],
            [
                CreateEndpoint("/repo/server", "partial-endpoint"),
                dynamicEndpoint
            ],
            [
                UniqueMatch(0, 0, ApiClientRequestEndpointMatchKind.ExactPath),
                UniqueMatch(1, 1, ApiClientRequestEndpointMatchKind.ExactPath),
                UniqueMatch(2, 0, ApiClientRequestEndpointMatchKind.UnresolvedEndpoint)
            ],
            [CreateModule("client", "/repo/client"), CreateModule("server", "/repo/server")]);

        var projection = UnifiedArchitectureGraphProjector.Project(context);

        projection.Edges.Should().BeEmpty();
        projection.UnprojectedEvidence.Should().HaveCount(3);
        projection.UnprojectedEvidence.Should().OnlyContain(item =>
            item.Reason == ArchitectureGraphUnprojectedReason.UnresolvedEvidence);
    }

    [Fact]
    public void Project_rejects_invalid_request_and_endpoint_occurrence_indexes()
    {
        var context = CreateContext(
            [CreateRequest("/repo/client", "valid-request")],
            [CreateEndpoint("/repo/server", "valid-endpoint")],
            [
                new ApiClientRequestEndpointMatch(
                    4,
                    ApiClientRequestEndpointMatchStatus.UniqueCandidate,
                    [new ApiClientRequestEndpointMatchCandidate(0, ApiClientRequestEndpointMatchKind.ExactPath, [])],
                    []),
                UniqueMatch(0, 7, ApiClientRequestEndpointMatchKind.ExactPath)
            ],
            [CreateModule("client", "/repo/client"), CreateModule("server", "/repo/server")]);

        var projection = UnifiedArchitectureGraphProjector.Project(context);

        projection.Edges.Should().BeEmpty();
        projection.UnprojectedEvidence.Should().HaveCount(2);
        projection.UnprojectedEvidence.Should().OnlyContain(item =>
            item.Reason == ArchitectureGraphUnprojectedReason.InvalidApiEvidenceReference);
    }

    [Fact]
    public void Project_rejects_missing_or_ambiguous_module_ownership()
    {
        var ambiguousContext = CreateContext(
            [CreateRequest("/repo/client", "ambiguous-owner")],
            [CreateEndpoint("/repo/server", "endpoint")],
            [UniqueMatch(0, 0, ApiClientRequestEndpointMatchKind.ExactPath)],
            [
                CreateModule("client-a", "/repo/client"),
                CreateModule("client-b", "/repo/client"),
                CreateModule("server", "/repo/server")
            ]);
        var missingContext = CreateContext(
            [CreateRequest("/repo/missing", "missing-owner")],
            [CreateEndpoint("/repo/server", "endpoint")],
            [UniqueMatch(0, 0, ApiClientRequestEndpointMatchKind.ExactPath)],
            [CreateModule("server", "/repo/server")]);

        var ambiguousProjection = UnifiedArchitectureGraphProjector.Project(ambiguousContext);
        var missingProjection = UnifiedArchitectureGraphProjector.Project(missingContext);

        ambiguousProjection.Edges.Should().BeEmpty();
        ambiguousProjection.UnprojectedEvidence.Should().ContainSingle(item =>
            item.Evidence.EvidenceId == "ambiguous-owner"
            && item.Reason == ArchitectureGraphUnprojectedReason.AmbiguousEndpointOwnership);
        missingProjection.Edges.Should().BeEmpty();
        missingProjection.UnprojectedEvidence.Should().ContainSingle(item =>
            item.Evidence.EvidenceId == "missing-owner"
            && item.Reason == ArchitectureGraphUnprojectedReason.MissingEndpoint);
    }

    [Fact]
    public void Project_preserves_test_scope_and_valid_self_loop_edges()
    {
        var context = CreateContext(
            [CreateRequest("/repo/test-module", "self-request")],
            [CreateEndpoint("/repo/test-module", "self-endpoint")],
            [UniqueMatch(0, 0, ApiClientRequestEndpointMatchKind.RouteTemplate)],
            [CreateModule("test-module", "/repo/test-module", isTest: true)]);

        var projection = UnifiedArchitectureGraphProjector.Project(context);

        var edge = projection.Edges.Should().ContainSingle().Subject;
        edge.SourceNodeId.Should().Be(edge.TargetNodeId);
        edge.Scope.Should().Be("Test");
        edge.Domain.Should().Be(ArchitectureGraphEdgeDomain.Api);
        projection.UnprojectedEvidence.Should().BeEmpty();
    }

    [Fact]
    public void Project_retains_requests_when_match_snapshot_is_missing()
    {
        var context = new ProjectArchitectureContext
        {
            Modules = [CreateModule("client", "/repo/client")],
            ApiClientRequestDiscoveryRegistry = new ApiClientRequestDiscoveryRegistry(
                [CreateRequest("/repo/client", "no-match-snapshot")])
        };

        var projection = UnifiedArchitectureGraphProjector.Project(context);

        projection.Edges.Should().BeEmpty();
        projection.UnprojectedEvidence.Should().ContainSingle(item =>
            item.Evidence.EvidenceId == "no-match-snapshot"
            && item.Reason == ArchitectureGraphUnprojectedReason.UnresolvedEvidence);
    }

    private static ProjectArchitectureContext CreateContext(
        IReadOnlyList<ApiClientRequestEvidence> requests,
        IReadOnlyList<ApiEndpointEvidence> endpoints,
        IReadOnlyList<ApiClientRequestEndpointMatch> matches,
        IReadOnlyList<ProjectModuleContext> modules) =>
        new()
        {
            Modules = modules.ToList(),
            ApiClientRequestDiscoveryRegistry = new ApiClientRequestDiscoveryRegistry(requests),
            ServerApiEndpointDiscoveryRegistry = new ServerApiEndpointDiscoveryRegistry(endpoints),
            ApiClientRequestEndpointMatchRegistry = new ApiClientRequestEndpointMatchRegistry(matches)
        };

    private static ApiClientRequestEvidence CreateRequest(string modulePath, string evidenceId) =>
        new()
        {
            EvidenceId = evidenceId,
            ProviderId = "client-provider",
            ModulePath = Path.GetFullPath(modulePath),
            ResolutionStatus = ApiClientRequestResolutionStatus.Resolved
        };

    private static ApiEndpointEvidence CreateEndpoint(string modulePath, string evidenceId) =>
        new()
        {
            EvidenceId = evidenceId,
            ProviderId = "server-provider",
            ModulePath = Path.GetFullPath(modulePath),
            ResolutionStatus = ApiEndpointResolutionStatus.Resolved
        };

    private static ProjectModuleContext CreateModule(string name, string path, bool isTest = false) =>
        new()
        {
            Name = name,
            Path = Path.GetFullPath(path),
            IsTestProject = isTest
        };

    private static ApiClientRequestEndpointMatch UniqueMatch(
        int requestIndex,
        int endpointIndex,
        ApiClientRequestEndpointMatchKind matchKind) =>
        new(
            requestIndex,
            ApiClientRequestEndpointMatchStatus.UniqueCandidate,
            [new ApiClientRequestEndpointMatchCandidate(endpointIndex, matchKind, [])],
            []);

    private static string ModuleNodeId(string path) =>
        $"module:{Uri.EscapeDataString(Path.GetFullPath(path))}";
}
