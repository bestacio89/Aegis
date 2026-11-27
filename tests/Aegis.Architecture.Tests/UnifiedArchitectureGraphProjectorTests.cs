using Aegis.Architecture.Analysis.Projection;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using FluentAssertions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class UnifiedArchitectureGraphProjectorTests
{
    [Fact]
    public void Project_preserves_build_and_semantic_domains_and_global_semantic_occurrence_identity()
    {
        var firstRun = CreateRun("run-1", "duplicate-id");
        var secondRun = CreateRun("run-2", "duplicate-id");
        var context = new ProjectArchitectureContext
        {
            Modules =
            [
                new ProjectModuleContext { Name = "api", Path = "/repo/api" },
                new ProjectModuleContext { Name = "domain", Path = "/repo/domain" }
            ],
            Dependencies =
            [
                new ArchitectureDependencyContext
                {
                    Source = "api",
                    SourceProjectPath = "/repo/api",
                    Target = "domain",
                    TargetProjectPath = "/repo/domain",
                    DependencyType = "ProjectReference",
                    Scope = "Production"
                }
            ],
            ArchitectureComponents =
            [
                new ArchitectureComponentContext { Id = "api-code", Name = "Api", QualifiedName = "Api", Language = "C#", BuildModulePath = "/repo/api" },
                new ArchitectureComponentContext { Id = "domain-code", Name = "Domain", QualifiedName = "Domain", Language = "C#", BuildModulePath = "/repo/domain" }
            ],
            ArchitectureComponentDependencies =
            [
                new ArchitectureComponentDependencyContext
                {
                    SourceComponentId = "api-code",
                    TargetComponentId = "domain-code",
                    Scope = "Production",
                    DependencyType = "SemanticComponentDependency",
                    RelationshipCount = 3,
                    ResolvedRelationshipCount = 1,
                    UnreferencedRelationshipCount = 2,
                    EvidenceFiles = ["/repo/src/Api.cs", "/repo/src/Generated.cs"],
                    RelationshipKinds = [SemanticRelationshipKind.Calls],
                    ContributingRelationships =
                    [
                        new SemanticRelationshipReference
                        {
                            RunId = "run-2",
                            RelationshipId = "duplicate-id",
                            RunIndex = 1,
                            RelationshipIndex = 0
                        }
                    ]
                }
            ],
            SemanticAnalysisRuns = [firstRun, secondRun]
        };
        context.RelationshipEvidenceRegistry =
            SemanticRelationshipEvidenceRegistry.Create(context.SemanticAnalysisRuns);

        var projection = UnifiedArchitectureGraphProjector.Project(context);

        projection.Edges.Should().HaveCount(2);
        projection.Edges.Should().ContainSingle(edge =>
            edge.Domain == ArchitectureGraphEdgeDomain.Build
            && edge.RelationshipKinds.Contains(ArchitectureRelationshipKind.BuildProjectReference));
        var semanticEdge = projection.Edges.Should().ContainSingle(edge =>
            edge.Domain == ArchitectureGraphEdgeDomain.NativeSemantic).Subject;
        semanticEdge.Evidence.Should().ContainSingle();
        semanticEdge.Evidence[0].OccurrenceIndex.Should().Be(1);
        semanticEdge.Evidence[0].EvidenceId.Should().Be("1:0");
        semanticEdge.RelationshipCount.Should().Be(3);
        semanticEdge.EvidenceFiles.Should().BeEquivalentTo(["/repo/src/Api.cs", "/repo/src/Generated.cs"]);
        projection.Nodes.Should().Contain(node => node.Id.StartsWith("code:", StringComparison.Ordinal));
        projection.UnprojectedEvidence.Should().BeEmpty();
    }

    [Fact]
    public void Project_projects_only_resolved_integrations_with_explicit_resource_identity()
    {
        var modulePath = Path.GetFullPath("/repo/api");
        var context = new ProjectArchitectureContext
        {
            Modules = [new ProjectModuleContext { Name = "api", Path = modulePath }],
            MessageIntegrationEvidenceRegistry = new IntegrationEvidenceRegistry<MessageIntegrationEvidence>(
            [
                new MessageIntegrationEvidence
                {
                    EvidenceId = "publish",
                    ProviderId = "shared-provider",
                    ModulePath = modulePath,
                    Operation = MessageIntegrationOperation.Publish,
                    DestinationIdentity = "orders",
                    ResolutionStatus = IntegrationEvidenceResolutionStatus.Resolved
                },
                new MessageIntegrationEvidence
                {
                    EvidenceId = "dynamic",
                    ProviderId = "shared-provider",
                    ModulePath = modulePath,
                    Operation = MessageIntegrationOperation.Publish,
                    DestinationExpression = "ResolveTopic()",
                    ResolutionStatus = IntegrationEvidenceResolutionStatus.Dynamic
                }
            ]),
            RpcIntegrationEvidenceRegistry = new IntegrationEvidenceRegistry<RpcIntegrationEvidence>(
            [
                new RpcIntegrationEvidence
                {
                    EvidenceId = "rpc-call",
                    ProviderId = "shared-provider",
                    ModulePath = modulePath,
                    Operation = RpcIntegrationOperation.Call,
                    TargetIdentity = "orders",
                    ResolutionStatus = IntegrationEvidenceResolutionStatus.Resolved
                }
            ]),
            DataAccessEvidenceRegistry = new IntegrationEvidenceRegistry<DataAccessEvidence>(
            [
                new DataAccessEvidence
                {
                    EvidenceId = "data-read",
                    ProviderId = "shared-provider",
                    ModulePath = modulePath,
                    Operation = DataIntegrationOperation.Read,
                    DataObjectIdentity = "orders",
                    ResolutionStatus = IntegrationEvidenceResolutionStatus.Resolved
                },
                new DataAccessEvidence
                {
                    EvidenceId = "data-read-resolved",
                    ProviderId = "shared-provider",
                    ModulePath = modulePath,
                    Operation = DataIntegrationOperation.Read,
                    StoreIdentity = "orders-db",
                    ResolutionStatus = IntegrationEvidenceResolutionStatus.Resolved
                }
            ])
        };

        context.IntegrationEvidenceCatalog = new IntegrationEvidenceCatalog(
            context.MessageIntegrationEvidenceRegistry,
            context.RpcIntegrationEvidenceRegistry,
            context.DataAccessEvidenceRegistry);

        var projection = UnifiedArchitectureGraphProjector.Project(context);

        projection.Edges.Should().HaveCount(3);
        projection.Edges.Should().Contain(edge => edge.Domain == ArchitectureGraphEdgeDomain.Messaging);
        projection.Edges.Should().Contain(edge => edge.Domain == ArchitectureGraphEdgeDomain.Rpc);
        var dataReadEdge = projection.Edges.Should()
            .ContainSingle(edge => edge.Domain == ArchitectureGraphEdgeDomain.DataAccess).Subject;
        dataReadEdge.SourceNodeId.Should().StartWith("resource:");
        dataReadEdge.TargetNodeId.Should().StartWith("module:");
        projection.Nodes.Where(node => node.Kind == ArchitectureGraphNodeKind.ExternalResource)
            .Select(node => node.Id)
            .Should().OnlyHaveUniqueItems();
        projection.UnprojectedEvidence.Should().HaveCount(2);
        projection.UnprojectedEvidence.Should().Contain(item =>
            item.Evidence.EvidenceId == "dynamic"
            && item.Reason == ArchitectureGraphUnprojectedReason.UnresolvedEvidence);
        projection.UnprojectedEvidence.Should().Contain(item =>
            item.Evidence.EvidenceId == "data-read"
            && item.Reason == ArchitectureGraphUnprojectedReason.MissingResourceIdentity);
    }

    [Fact]
    public void Project_preserves_test_scope_and_rejects_ambiguous_module_ownership()
    {
        var context = new ProjectArchitectureContext
        {
            Modules =
            [
                new ProjectModuleContext { Name = "api-tests", Path = "/repo/tests", IsTestProject = true },
                new ProjectModuleContext { Name = "duplicate-a", Path = "/repo/shared" },
                new ProjectModuleContext { Name = "duplicate-b", Path = "/repo/shared" }
            ],
            Dependencies =
            [
                new ArchitectureDependencyContext
                {
                    Source = "api-tests",
                    SourceProjectPath = "/repo/tests",
                    Target = "api-tests",
                    TargetProjectPath = "/repo/tests",
                    DependencyType = "ProjectReference",
                    Scope = "Test"
                }
            ],
            MessageIntegrationEvidenceRegistry = new IntegrationEvidenceRegistry<MessageIntegrationEvidence>(
            [
                new MessageIntegrationEvidence
                {
                    EvidenceId = "ambiguous-owner",
                    ProviderId = "messaging-provider",
                    ModulePath = "/repo/shared",
                    Operation = MessageIntegrationOperation.Publish,
                    DestinationIdentity = "events",
                    ResolutionStatus = IntegrationEvidenceResolutionStatus.Resolved
                }
            ])
        };

        var projection = UnifiedArchitectureGraphProjector.Project(context);

        var buildEdge = projection.Edges.Should().ContainSingle().Subject;
        buildEdge.Scope.Should().Be("Test");
        projection.UnprojectedEvidence.Should().ContainSingle(item =>
            item.Evidence.EvidenceId == "ambiguous-owner"
            && item.Reason == ArchitectureGraphUnprojectedReason.AmbiguousEndpointOwnership);
        projection.Nodes.Should().NotContain(node => node.Name == "duplicate-a" || node.Name == "duplicate-b");
    }


    [Fact]
    public void Project_attaches_unique_architectural_role_to_code_container_nodes()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureComponents =
            [
                new ArchitectureComponentContext
                {
                    Id = "java-package:domain",
                    Name = "domain",
                    QualifiedName = "sample.domain",
                    Language = "Java",
                    ComponentKind = "Package",
                    ProductionSymbolCount = 2
                },
                new ArchitectureComponentContext
                {
                    Id = "java-package:application",
                    Name = "application",
                    QualifiedName = "sample.application",
                    Language = "Java",
                    ComponentKind = "Package",
                    ProductionSymbolCount = 2
                },
                new ArchitectureComponentContext
                {
                    Id = "java-package:ambiguous",
                    Name = "ambiguous",
                    QualifiedName = "sample.ambiguous",
                    Language = "Java",
                    ComponentKind = "Package",
                    ProductionSymbolCount = 1
                }
            ],
            ArchitecturalComponents =
            [
                new ArchitecturalComponentContext { Id = "logical-domain", Name = "Domain", Role = "Domain", ProductionSymbolCount = 2 },
                new ArchitecturalComponentContext { Id = "logical-business", Name = "Business", Role = "Business", ProductionSymbolCount = 2 }
            ],
            ArchitecturalComponentMemberships =
            [
                new ArchitecturalComponentMembership
                {
                    CodeComponentId = "java-package:domain",
                    ArchitecturalComponentId = "logical-domain",
                    ClassificationSource = ArchitectureClassificationSource.Convention
                },
                new ArchitecturalComponentMembership
                {
                    CodeComponentId = "java-package:application",
                    ArchitecturalComponentId = "logical-business",
                    ClassificationSource = ArchitectureClassificationSource.Convention
                },
                new ArchitecturalComponentMembership
                {
                    CodeComponentId = "java-package:ambiguous",
                    ArchitecturalComponentId = "logical-domain",
                    ClassificationSource = ArchitectureClassificationSource.Convention
                },
                new ArchitecturalComponentMembership
                {
                    CodeComponentId = "java-package:ambiguous",
                    ArchitecturalComponentId = "logical-business",
                    ClassificationSource = ArchitectureClassificationSource.Convention
                }
            ]
        };

        var projection = UnifiedArchitectureGraphProjector.Project(context);
        var domainNode = projection.Nodes.Should().ContainSingle(node => node.Id == "code:java-package%3Adomain").Subject;
        var businessNode = projection.Nodes.Should().ContainSingle(node => node.Id == "code:java-package%3Aapplication").Subject;
        var ambiguousNode = projection.Nodes.Should().ContainSingle(node => node.Id == "code:java-package%3Aambiguous").Subject;

        domainNode.Name.Should().Be("sample.domain");
        domainNode.ComponentKind.Should().Be("Package");
        domainNode.ArchitecturalRole.Should().Be("Domain");
        businessNode.ComponentKind.Should().Be("Package");
        businessNode.ArchitecturalRole.Should().Be("Business");
        ambiguousNode.ComponentKind.Should().Be("Package");
        ambiguousNode.ArchitecturalRole.Should().BeNull();
    }

    private static SemanticAnalysisRun CreateRun(string runId, string relationshipId) =>
        new()
        {
            RunId = runId,
            Language = "C#",
            EngineId = "test-provider",
            ProjectPath = "/repo",
            Status = SemanticAnalysisStatus.Completed,
            Relationships =
            [
                new SemanticRelationship
                {
                    Id = relationshipId,
                    SourceSymbolId = "source",
                    TargetSymbolId = "target",
                    Kind = SemanticRelationshipKind.Calls,
                    ResolutionStatus = SemanticResolutionStatus.Resolved
                }
            ]
        };
}
