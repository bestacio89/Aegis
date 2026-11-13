using Aegis.Architecture.Analysis.Detectors;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitecturalComponentDependencyProjectionTests
{
    [Fact]
    public void Aggregates_code_container_edges_and_preserves_internal_coupling_and_evidence()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitecturalComponents =
            [
                new ArchitecturalComponentContext { Id = "core", Name = "Core" },
                new ArchitecturalComponentContext { Id = "billing", Name = "Billing" }
            ],
            ArchitecturalComponentMemberships =
            [
                Membership("core-domain", "core"),
                Membership("core-application", "core"),
                Membership("billing-api", "billing"),
                Membership("unclassified", null)
            ],
            ArchitectureComponentDependencies =
            [
                Dependency("core-domain", "core-application", "Production", 2, "run-1", "r-1", "r-2"),
                Dependency("core-application", "billing-api", "Production", 3, "run-2", "r-3", "r-4", "r-5"),
                Dependency("core-domain", "billing-api", "Production", 2, "run-3", "r-6", "r-7"),
                Dependency("core-domain", "billing-api", "Test", 1, "test-run", "test-r"),
                Dependency("billing-api", "unclassified", "Production", 4, "run-4", "r-8", "r-9", "r-10", "r-11")
            ]
        };

        ArchitecturalComponentDependencyProjector.Project(context);

        Assert.Equal(3, context.ArchitecturalComponentDependencies.Count);

        var internalEdge = Assert.Single(context.InternalArchitecturalComponentDependencies);
        Assert.Equal("core", internalEdge.SourceComponentId);
        Assert.Equal("core", internalEdge.TargetComponentId);
        Assert.Equal(2, internalEdge.RelationshipCount);
        Assert.Single(internalEdge.ContributingCodeContainerDependencies);

        var productionEdge = context.ProductionArchitecturalComponentDependencies
            .Single(edge => !edge.IsInternal);
        Assert.Equal("core", productionEdge.SourceComponentId);
        Assert.Equal("billing", productionEdge.TargetComponentId);
        Assert.Equal(5, productionEdge.RelationshipCount);
        Assert.Equal(5, productionEdge.ContributingRelationships.Count);
        Assert.Equal(2, productionEdge.ContributingCodeContainerDependencies.Count);
        Assert.Equal(5, productionEdge.ResolvedRelationshipCount);
        Assert.Contains("/src/core/Domain.cs", productionEdge.EvidenceFiles);
        Assert.Contains("/src/core/Application.cs", productionEdge.EvidenceFiles);

        var testEdge = Assert.Single(context.TestArchitecturalComponentDependencies);
        Assert.Equal(1, testEdge.RelationshipCount);
        Assert.Equal("test-run", Assert.Single(testEdge.ContributingRelationships).RunId);

        var diagnostic = Assert.Single(context.ArchitecturalDependencyProjectionDiagnostics);
        Assert.Equal("ArchitecturalDependencyUnclassifiedEndpoint", diagnostic.Code);
        Assert.Equal(4, diagnostic.RelationshipCount);
        Assert.True(diagnostic.AffectsCoverage);
    }

    [Fact]
    public void Semantic_projection_retains_relationships_within_the_same_code_container()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aegis-internal-edge-{Guid.NewGuid():N}");
        var file = Path.Combine(root, "OrderService.java");
        var context = new ProjectArchitectureContext
        {
            ProjectName = "sample",
            RootPath = root,
            SemanticAnalysisRuns =
            [
                new SemanticAnalysisRun
                {
                    RunId = "run-internal",
                    Language = "Java",
                    EngineId = "test-provider",
                    ProjectPath = root,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "service",
                            Name = "OrderService",
                            ContainerName = "sample.application",
                            ContainerKind = "Package",
                            Language = "Java",
                            ProjectPath = root,
                            FilePath = file,
                            Origin = SemanticSymbolOrigin.Source
                        },
                        new SemanticSymbol
                        {
                            Id = "helper",
                            Name = "OrderValidator",
                            ContainerName = "sample.application",
                            ContainerKind = "Package",
                            Language = "Java",
                            ProjectPath = root,
                            FilePath = file,
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ],
                    Relationships =
                    [
                        new SemanticRelationship
                        {
                            Id = "service-calls-validator",
                            SourceSymbolId = "service",
                            TargetSymbolId = "helper",
                            Kind = SemanticRelationshipKind.Calls,
                            ResolutionStatus = SemanticResolutionStatus.Resolved
                        }
                    ]
                }
            ]
        };

        SemanticArchitectureComponentDetector.Analyze(context);

        var edge = Assert.Single(context.ArchitectureComponentDependencies);
        Assert.True(edge.IsInternal);
        Assert.Equal(edge.SourceComponentId, edge.TargetComponentId);
        Assert.Equal("service-calls-validator",
            Assert.Single(edge.ContributingRelationships).RelationshipId);
    }

    private static ArchitecturalComponentMembership Membership(
        string codeComponentId,
        string? architecturalComponentId) =>
        new()
        {
            CodeComponentId = codeComponentId,
            ArchitecturalComponentId = architecturalComponentId,
            ClassificationSource = architecturalComponentId is null
                ? ArchitectureClassificationSource.Unclassified
                : ArchitectureClassificationSource.ExplicitMapping,
            Confidence = architecturalComponentId is null ? 0 : 1
        };

    private static ArchitectureComponentDependencyContext Dependency(
        string source,
        string target,
        string scope,
        int relationshipCount,
        string runId,
        params string[] relationshipIds) =>
        new()
        {
            SourceComponentId = source,
            SourceComponentName = source,
            TargetComponentId = target,
            TargetComponentName = target,
            Scope = scope,
            RelationshipCount = relationshipCount,
            ResolvedRelationshipCount = relationshipCount,
            ContributingRelationships = relationshipIds.Select(id =>
                new SemanticRelationshipReference
                {
                    RunId = runId,
                    RelationshipId = id
                }).ToList(),
            RelationshipKinds = [SemanticRelationshipKind.References],
            EvidenceFiles =
            [
                source == "core-domain" ? "/src/core/Domain.cs" : "/src/core/Application.cs"
            ]
        };
}
