using Aegis.Architecture.Analysis.Projection;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using FluentAssertions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class PolyglotEvidenceProjectionCorpusTests
{
    [Fact]
    public void Project_preserves_all_supported_evidence_domains_in_one_repository_snapshot()
    {
        const string clientPath = "/repo/client";
        const string serverPath = "/repo/server";
        const string ordersPath = "/repo/orders";
        const string persistencePath = "/repo/persistence";

        var semanticRun = new SemanticAnalysisRun
        {
            RunId = "orders-semantic-run",
            Language = "C#",
            EngineId = "roslyn",
            ProjectPath = ordersPath,
            Status = SemanticAnalysisStatus.Completed,
            Relationships =
            [
                new SemanticRelationship
                {
                    Id = "orders-calls-persistence",
                    SourceSymbolId = "orders-service",
                    TargetSymbolId = "orders-repository",
                    Kind = SemanticRelationshipKind.Calls,
                    ResolutionStatus = SemanticResolutionStatus.Resolved
                }
            ]
        };

        var context = new ProjectArchitectureContext
        {
            Modules =
            [
                new ProjectModuleContext { Name = "client", Path = clientPath },
                new ProjectModuleContext { Name = "server", Path = serverPath },
                new ProjectModuleContext { Name = "orders", Path = ordersPath },
                new ProjectModuleContext { Name = "persistence", Path = persistencePath }
            ],
            Dependencies =
            [
                new ArchitectureDependencyContext
                {
                    Source = "client",
                    SourceProjectPath = clientPath,
                    Target = "orders",
                    TargetProjectPath = ordersPath,
                    DependencyType = "ProjectReference",
                    Scope = "Production"
                }
            ],
            ArchitectureComponents =
            [
                new ArchitectureComponentContext
                {
                    Id = "orders-code",
                    Name = "Orders",
                    QualifiedName = "Orders",
                    Language = "C#",
                    ComponentKind = "Namespace",
                    BuildModulePath = ordersPath,
                    ProductionSymbolCount = 2
                },
                new ArchitectureComponentContext
                {
                    Id = "persistence-code",
                    Name = "Persistence",
                    QualifiedName = "Persistence",
                    Language = "C#",
                    ComponentKind = "Namespace",
                    BuildModulePath = persistencePath,
                    ProductionSymbolCount = 1
                }
            ],
            ArchitectureComponentDependencies =
            [
                new ArchitectureComponentDependencyContext
                {
                    SourceComponentId = "orders-code",
                    TargetComponentId = "persistence-code",
                    Scope = "Production",
                    DependencyType = "SemanticComponentDependency",
                    RelationshipCount = 1,
                    ResolvedRelationshipCount = 1,
                    ContributingRelationships =
                    [
                        new SemanticRelationshipReference
                        {
                            RunId = semanticRun.RunId,
                            RelationshipId = "orders-calls-persistence",
                            RunIndex = 0,
                            RelationshipIndex = 0
                        }
                    ]
                }
            ],
            SemanticAnalysisRuns = [semanticRun],
            MessageIntegrationEvidenceRegistry =
                new IntegrationEvidenceRegistry<MessageIntegrationEvidence>(
                [
                    new MessageIntegrationEvidence
                    {
                        EvidenceId = "orders-publish",
                        Language = "Java",
                        Framework = "Spring Kafka",
                        ProviderId = "java-jdt",
                        ModulePath = ordersPath,
                        Operation = MessageIntegrationOperation.Publish,
                        MessageTypeIdentity = "orders.OrderSubmitted",
                        DestinationIdentity = "orders.events",
                        ResolutionStatus = IntegrationEvidenceResolutionStatus.Resolved
                    }
                ]),
            RpcIntegrationEvidenceRegistry =
                new IntegrationEvidenceRegistry<RpcIntegrationEvidence>(
                [
                    new RpcIntegrationEvidence
                    {
                        EvidenceId = "orders-rpc-call",
                        Language = "Kotlin",
                        Framework = "gRPC",
                        ProviderId = "kotlin-k2-ir",
                        ModulePath = ordersPath,
                        Operation = RpcIntegrationOperation.Call,
                        Protocol = "gRPC",
                        ServiceIdentity = "inventory.InventoryService",
                        MethodIdentity = "GetStock",
                        TargetIdentity = "inventory-service",
                        ResolutionStatus = IntegrationEvidenceResolutionStatus.Resolved
                    }
                ]),
            DataAccessEvidenceRegistry =
                new IntegrationEvidenceRegistry<DataAccessEvidence>(
                [
                    new DataAccessEvidence
                    {
                        EvidenceId = "orders-data-read",
                        Language = "C#",
                        Framework = "Entity Framework Core",
                        ProviderId = "roslyn-efcore",
                        ModulePath = ordersPath,
                        Operation = DataIntegrationOperation.Read,
                        Technology = "Entity Framework Core",
                        StoreIdentity = "orders-db",
                        DataObjectIdentity = "Orders.Order",
                        ResolutionStatus = IntegrationEvidenceResolutionStatus.Resolved
                    }
                ]),
            ApiClientRequestDiscoveryRegistry = new ApiClientRequestDiscoveryRegistry(
            [
                new ApiClientRequestEvidence
                {
                    EvidenceId = "client-orders-request",
                    Language = "TypeScript",
                    Framework = "Fetch",
                    ProviderId = "typescript-compiler-api",
                    ModulePath = clientPath,
                    HttpMethod = "GET",
                    RequestUriExpression = "/api/orders",
                    ResolutionStatus = ApiClientRequestResolutionStatus.Resolved
                }
            ]),
            ServerApiEndpointDiscoveryRegistry = new ServerApiEndpointDiscoveryRegistry(
            [
                new ApiEndpointEvidence
                {
                    EvidenceId = "server-orders-endpoint",
                    Language = "Java",
                    Framework = "Spring MVC",
                    ProviderId = "eclipse-jdt",
                    ModulePath = serverPath,
                    HttpMethods = ["GET"],
                    RouteTemplate = "/api/orders",
                    ResolutionStatus = ApiEndpointResolutionStatus.Resolved
                }
            ]),
            ApiClientRequestEndpointMatchRegistry = new ApiClientRequestEndpointMatchRegistry(
            [
                new ApiClientRequestEndpointMatch(
                    0,
                    ApiClientRequestEndpointMatchStatus.UniqueCandidate,
                    [
                        new ApiClientRequestEndpointMatchCandidate(
                            0,
                            ApiClientRequestEndpointMatchKind.ExactPath,
                            ["Exact method and path"])
                    ],
                    ["Unique static endpoint candidate"])
            ])
        };

        context.RelationshipEvidenceRegistry =
            SemanticRelationshipEvidenceRegistry.Create(context.SemanticAnalysisRuns);

        var projection = UnifiedArchitectureGraphProjector.Project(context);

        projection.Edges.Should().HaveCount(6);
        projection.UnprojectedEvidence.Should().BeEmpty();

        projection.Edges.Should().ContainSingle(edge =>
            edge.Domain == ArchitectureGraphEdgeDomain.Build
            && edge.Evidence.Any(evidence =>
                evidence.Domain == ArchitectureGraphEvidenceDomain.BuildDependency
                && evidence.OccurrenceIndex == 0));

        projection.Edges.Should().ContainSingle(edge =>
            edge.Domain == ArchitectureGraphEdgeDomain.NativeSemantic
            && edge.Evidence.Any(evidence =>
                evidence.Domain == ArchitectureGraphEvidenceDomain.SemanticRelationship
                && evidence.OccurrenceIndex == 0
                && evidence.EvidenceId == "0:0"));

        projection.Edges.Should().ContainSingle(edge =>
            edge.Domain == ArchitectureGraphEdgeDomain.Messaging
            && edge.Evidence.Any(evidence =>
                evidence.Domain == ArchitectureGraphEvidenceDomain.Messaging
                && evidence.OccurrenceIndex == 0
                && evidence.EvidenceId == "orders-publish"));

        projection.Edges.Should().ContainSingle(edge =>
            edge.Domain == ArchitectureGraphEdgeDomain.Rpc
            && edge.Evidence.Any(evidence =>
                evidence.Domain == ArchitectureGraphEvidenceDomain.Rpc
                && evidence.OccurrenceIndex == 0
                && evidence.EvidenceId == "orders-rpc-call"));

        projection.Edges.Should().ContainSingle(edge =>
            edge.Domain == ArchitectureGraphEdgeDomain.DataAccess
            && edge.Evidence.Any(evidence =>
                evidence.Domain == ArchitectureGraphEvidenceDomain.DataAccess
                && evidence.OccurrenceIndex == 0
                && evidence.EvidenceId == "orders-data-read"));

        projection.Edges.Should().ContainSingle(edge =>
            edge.Domain == ArchitectureGraphEdgeDomain.Api
            && edge.Evidence.Any(evidence =>
                evidence.Domain == ArchitectureGraphEvidenceDomain.ApiClientRequest
                && evidence.OccurrenceIndex == 0
                && evidence.EvidenceId == "client-orders-request")
            && edge.Evidence.Any(evidence =>
                evidence.Domain == ArchitectureGraphEvidenceDomain.ApiEndpoint
                && evidence.OccurrenceIndex == 0
                && evidence.EvidenceId == "server-orders-endpoint"));
    }
}
