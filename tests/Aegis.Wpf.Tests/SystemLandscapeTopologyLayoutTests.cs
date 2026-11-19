using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;
using FluentAssertions;
using Xunit;

namespace Aegis.Wpf.Tests;

public sealed class SystemLandscapeTopologyLayoutTests
{
    [Fact]
    public void Arrange_IsDeterministicAndIndependentOfRelationshipDensity()
    {
        var first = CreateNodes();
        var second = CreateNodes();
        var sparseEdges = new[]
        {
            new SystemLandscapeTopologyEdgeViewModel
            {
                Id = "edge-a",
                SourceNodeId = "region-a",
                TargetNodeId = "region-b",
                Domain = "DataAccess",
                Scope = "Production",
                RelationshipCount = 1
            }
        };
        var denseEdges = Enumerable.Range(0, 25)
            .Select(index => new SystemLandscapeTopologyEdgeViewModel
            {
                Id = $"edge-{index}",
                SourceNodeId = "region-a",
                TargetNodeId = "region-b",
                Domain = "DataAccess",
                Scope = $"Scope-{index}",
                RelationshipCount = 1000
            })
            .ToArray();

        SystemLandscapeTopologyLayout.Arrange(first, sparseEdges);
        SystemLandscapeTopologyLayout.Arrange(second, denseEdges);

        first.Select(node => new { node.Id, node.Left, node.Top })
            .Should().BeEquivalentTo(second.Select(node => new { node.Id, node.Left, node.Top }));

        var repeated = CreateNodes();
        SystemLandscapeTopologyLayout.Arrange(repeated, sparseEdges);
        var firstPlacement = repeated.Select(node => new { node.Id, node.Left, node.Top, node.Width, node.Height }).ToArray();
        SystemLandscapeTopologyLayout.Arrange(repeated, denseEdges);
        repeated.Select(node => new { node.Id, node.Left, node.Top, node.Width, node.Height })
            .Should().BeEquivalentTo(firstPlacement);
    }

    [Fact]
    public void Arrange_CreatesOrthogonalRoutesForKnownEndpoints()
    {
        var nodes = CreateNodes();
        var edge = new SystemLandscapeTopologyEdgeViewModel
        {
            Id = "edge-a",
            SourceNodeId = "region-a",
            TargetNodeId = "region-b",
            Domain = "DataAccess",
            Scope = "Production",
            RelationshipCount = 1
        };

        SystemLandscapeTopologyLayout.Arrange(nodes, [edge]);

        edge.Route.Should().NotBeSameAs(System.Windows.Media.Geometry.Empty);
        edge.Route.Bounds.Width.Should().BeGreaterThan(0);
        edge.Route.Bounds.Height.Should().BeGreaterThan(0);
        edge.ArrowHead.Bounds.Width.Should().BeGreaterThan(0);
        edge.ArrowHead.Bounds.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Arrange_NestsChildRegionsAndTheirImplementationBoundaries()
    {
        var nodes = new List<SystemLandscapeTopologyNodeViewModel>
        {
            new() { Id = "root", Name = "System", Category = "System", IsRegion = true },
            new() { Id = "domain", Name = "Orders", Category = "Domain", IsRegion = true, ParentRegionId = "root", HierarchyDepth = 1 },
            new() { Id = "package", Name = "Orders.Persistence", Category = "Package", ParentRegionId = "domain" }
        };

        SystemLandscapeTopologyLayout.Arrange(nodes, []);

        var root = nodes.Single(node => node.Id == "root");
        var domain = nodes.Single(node => node.Id == "domain");
        var package = nodes.Single(node => node.Id == "package");
        domain.Left.Should().BeGreaterThan(root.Left);
        domain.Top.Should().BeGreaterThan(root.Top);
        package.Left.Should().BeGreaterThan(domain.Left);
        package.Top.Should().BeGreaterThan(domain.Top);
        root.Width.Should().BeGreaterThan(domain.Width);
        root.Height.Should().BeGreaterThan(domain.Height);
    }

    [Fact]
    public void Arrange_PromotesCyclicRegionsWithoutRecursionFailure()
    {
        var nodes = new List<SystemLandscapeTopologyNodeViewModel>
        {
            new() { Id = "a", Name = "A", Category = "Domain", IsRegion = true, ParentRegionId = "b" },
            new() { Id = "b", Name = "B", Category = "Domain", IsRegion = true, ParentRegionId = "a" }
        };

        var action = () => SystemLandscapeTopologyLayout.Arrange(nodes, []);

        action.Should().NotThrow();
        nodes.Select(node => node.Top).Distinct().Should().HaveCount(2);
    }

    private static List<SystemLandscapeTopologyNodeViewModel> CreateNodes() =>
    [
        new() { Id = "region-a", Name = "A", Category = "Domain", IsRegion = true },
        new() { Id = "region-b", Name = "B", Category = "Domain", IsRegion = true }
    ];
}