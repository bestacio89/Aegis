using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;
using Xunit;

namespace Aegis.Wpf.Tests;

public sealed class SystemLandscapeViewModelTests
{
    [Fact]
    public void Update_BuildsNestedRegionsAndAttachesOnlyUnambiguousMemberships()
    {
        var context = CreateContext(
            [Logical("system", "System"), Logical("domain", "Domain"), Code("code-domain", "Example.Domain", "C#")],
            [],
            []);
        context.ArchitecturalComponents =
        [
            new ArchitecturalComponentContext { Id = "system", Name = "System" },
            new ArchitecturalComponentContext { Id = "domain", Name = "Domain", ParentId = "system", Role = "Domain" }
        ];
        context.ArchitecturalComponentMemberships =
        [
            new ArchitecturalComponentMembership
            {
                CodeComponentId = "code-domain",
                ArchitecturalComponentId = "domain",
                ClassificationSource = ArchitectureClassificationSource.ExplicitMapping,
                Confidence = 1,
                Explanation = "Explicit architecture map"
            }
        ];

        var viewModel = new SystemLandscapeViewModel();
        viewModel.Update(context);

        var root = Assert.Single(viewModel.RootRegions);
        Assert.Equal("system", root.Id);
        var child = Assert.Single(root.Children);
        Assert.Equal("domain", child.Id);
        Assert.Equal("code-domain", Assert.Single(child.CodeContainers).Id);
        Assert.Equal(2, viewModel.LogicalRegionCount);
        Assert.Equal(1, viewModel.CodeContainerCount);
    }

    [Fact]
    public void Update_DoesNotChooseBetweenAmbiguousMemberships()
    {
        var context = CreateContext(
            [Logical("domain-a", "A"), Logical("domain-b", "B"), Code("code", "Example", "Java")],
            [],
            []);
        context.ArchitecturalComponents =
        [
            new ArchitecturalComponentContext { Id = "domain-a", Name = "A" },
            new ArchitecturalComponentContext { Id = "domain-b", Name = "B" }
        ];
        context.ArchitecturalComponentMemberships =
        [
            new ArchitecturalComponentMembership { CodeComponentId = "code", ArchitecturalComponentId = "domain-a", ClassificationSource = ArchitectureClassificationSource.Evidence, Confidence = 0.8 },
            new ArchitecturalComponentMembership { CodeComponentId = "code", ArchitecturalComponentId = "domain-b", ClassificationSource = ArchitectureClassificationSource.Evidence, Confidence = 0.9 }
        ];

        var viewModel = new SystemLandscapeViewModel();
        viewModel.Update(context);

        Assert.Empty(viewModel.RootRegions.Single(region => region.Id == "domain-a").CodeContainers);
        Assert.Equal("Ambiguous", Assert.Single(viewModel.UnclassifiedCodeContainers).ClassificationSource);
        Assert.Contains(viewModel.ProjectionDiagnostics, message => message.Contains("multiple membership records", StringComparison.Ordinal));
    }

    [Fact]
    public void Update_PreservesGraphDomainsDirectionAndEvidenceOccurrenceCounts()
    {
        var context = CreateContext(
            [Logical("service", "Service"), Resource("store", "Orders DB")],
            [
                new ArchitectureGraphEdge
                {
                    Id = "edge-data",
                    SourceNodeId = "service",
                    TargetNodeId = "store",
                    Domain = ArchitectureGraphEdgeDomain.DataAccess,
                    Scope = "Production",
                    ProviderRelationshipKind = "Write",
                    Evidence = [new ArchitectureGraphEvidenceReference { Domain = ArchitectureGraphEvidenceDomain.DataAccess, OccurrenceIndex = 4, EvidenceId = "evidence-1", ProviderId = "ef-core" }]
                }
            ],
            []);
        context.ArchitecturalComponents = [new ArchitecturalComponentContext { Id = "service", Name = "Service" }];

        var viewModel = new SystemLandscapeViewModel();
        viewModel.Update(context);

        var relationship = Assert.Single(viewModel.Relationships);
        Assert.Equal("DataAccess", relationship.Domain);
        Assert.Equal("Service → Orders DB", relationship.Connection);
        Assert.Equal(1, relationship.EvidenceOccurrenceCount);
        Assert.Equal("Write", relationship.RelationshipKind);
    }

    [Fact]
    public void Update_SeparatesDeclaredIntegrationsAndUnprojectedEvidenceFromResolvedRelationships()
    {
        var unresolved = new ArchitectureGraphUnprojectedEvidence
        {
            Evidence = new ArchitectureGraphEvidenceReference
            {
                Domain = ArchitectureGraphEvidenceDomain.ApiClientRequest,
                OccurrenceIndex = 2,
                EvidenceId = "request-2",
                ProviderId = "typescript-fetch"
            },
            Reason = ArchitectureGraphUnprojectedReason.AmbiguousApiMatch,
            Explanation = "Multiple endpoint candidates."
        };
        var context = CreateContext([Logical("service", "Service")], [], [unresolved]);
        context.ApplicationIntegrations =
        [
            new ArchitectureIntegrationContext
            {
                SourceModuleName = "Web",
                TargetModuleName = "Service",
                Protocol = "HTTP",
                EvidenceOrigin = "UserDefined"
            }
        ];

        var viewModel = new SystemLandscapeViewModel();
        viewModel.Update(context);

        Assert.Empty(viewModel.Relationships);
        Assert.Single(viewModel.UnprojectedEvidence);
        Assert.Single(viewModel.DeclaredIntegrations);
        Assert.Equal(1, viewModel.UnprojectedEvidenceCount);
        Assert.Equal(1, viewModel.DeclaredIntegrationCount);
    }

    [Fact]
    public void Update_RendersCyclicAndOrphanedRegionsAsRootsWithoutRecursionFailure()
    {
        var context = CreateContext([Logical("a", "A"), Logical("b", "B"), Logical("orphan", "Orphan")], [], []);
        context.ArchitecturalComponents =
        [
            new ArchitecturalComponentContext { Id = "a", Name = "A", ParentId = "b" },
            new ArchitecturalComponentContext { Id = "b", Name = "B", ParentId = "a" },
            new ArchitecturalComponentContext { Id = "orphan", Name = "Orphan", ParentId = "missing" }
        ];

        var viewModel = new SystemLandscapeViewModel();
        viewModel.Update(context);

        Assert.Equal(3, viewModel.RootRegions.Count);
        Assert.Contains(viewModel.ProjectionDiagnostics, message => message.Contains("parent cycle", StringComparison.Ordinal));
        Assert.Contains(viewModel.ProjectionDiagnostics, message => message.Contains("missing parent", StringComparison.Ordinal));
    }

    private static ProjectArchitectureContext CreateContext(
        IEnumerable<ArchitectureGraphNode> nodes,
        IEnumerable<ArchitectureGraphEdge> edges,
        IEnumerable<ArchitectureGraphUnprojectedEvidence> unprojected) =>
        new()
        {
            ProjectName = "Test system",
            ArchitectureGraphProjection = new ArchitectureGraphProjection(nodes, edges, unprojected)
        };

    private static ArchitectureGraphNode Logical(string id, string name) =>
        new() { Id = id, Name = name, Kind = ArchitectureGraphNodeKind.LogicalComponent };

    private static ArchitectureGraphNode Code(string id, string name, string language) =>
        new() { Id = id, Name = name, Kind = ArchitectureGraphNodeKind.CodeComponent, Language = language, ComponentKind = "Package" };

    private static ArchitectureGraphNode Resource(string id, string name) =>
        new() { Id = id, Name = name, Kind = ArchitectureGraphNodeKind.ExternalResource, ResourceKind = "DataStore", ResourceProvider = "PostgreSQL", ResourceIdentity = "orders" };
}
