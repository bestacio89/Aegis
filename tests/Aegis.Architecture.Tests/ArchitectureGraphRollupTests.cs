using Aegis.Architecture.Analysis.Projection;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using FluentAssertions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureGraphRollupTests
{
    [Fact]
    public void Apply_feature_first_packages_rolls_up_leaf_edges_and_counts_collapsed_relationships_as_internal()
    {
        var projection = CreateProjection(
            [
                Package("web", "shop.orders.web"),
                Package("persistence", "shop.orders.persistence"),
                Package("billing", "shop.billing.api")
            ],
            [
                Edge("orders-internal", "web", "persistence", "Production", 1, ArchitectureRelationshipKind.MethodCall, 0),
                Edge("orders-billing-a", "web", "billing", "Production", 2, ArchitectureRelationshipKind.MethodCall, 1),
                Edge("orders-billing-b", "persistence", "billing", "Production", 3, ArchitectureRelationshipKind.TypeReference, 2)
            ]);

        var result = ArchitectureGraphRollup.Apply(projection, "shop", 1);

        result.Nodes.Should().ContainSingle(node => node.Name == "orders")
            .Which.InternalRelationshipCounts.Should().ContainKey("Production").WhoseValue.Should().Be(1);
        result.Nodes.Should().ContainSingle(node => node.Name == "billing");
        var edge = result.Edges.Should().ContainSingle().Subject;
        edge.SourceGroupKey.Should().Be(result.Nodes.Single(node => node.Name == "orders").GroupKey);
        edge.TargetGroupKey.Should().Be(result.Nodes.Single(node => node.Name == "billing").GroupKey);
        edge.RelationshipCount.Should().Be(5);
        edge.RelationshipKinds.Should().BeEquivalentTo(
            [ArchitectureRelationshipKind.MethodCall, ArchitectureRelationshipKind.TypeReference]);
        edge.Evidence.Should().HaveCount(2);
        edge.LeafEdgeIds.Should().BeEquivalentTo(["orders-billing-a", "orders-billing-b"]);
    }

    [Fact]
    public void Apply_layer_first_packages_groups_by_first_qualified_name_segment_without_explicit_base()
    {
        var projection = CreateProjection(
            [Package("controller", "controller.admin"), Package("service", "service.impl")],
            [Edge("controller-service", "controller", "service", "Production", 1, ArchitectureRelationshipKind.MethodCall, 0)]);

        var result = ArchitectureGraphRollup.Apply(projection, null, 1);

        result.Nodes.Select(node => node.Name).Should().BeEquivalentTo(["controller", "service"]);
        result.Edges.Should().ContainSingle().Which.RelationshipCount.Should().Be(1);
    }

    [Fact]
    public void Apply_keeps_production_and_test_relationships_in_separate_aggregates()
    {
        var projection = CreateProjection(
            [Package("a", "shop.orders.web"), Package("b", "shop.billing.api")],
            [
                Edge("production", "a", "b", "Production", 2, ArchitectureRelationshipKind.MethodCall, 0),
                Edge("test", "a", "b", "Test", 3, ArchitectureRelationshipKind.TypeReference, 1)
            ]);

        var result = ArchitectureGraphRollup.Apply(projection, "shop", 1);

        result.Edges.Should().HaveCount(2);
        result.Edges.Select(edge => edge.Scope).Should().BeEquivalentTo(["Production", "Test"]);
        result.Edges.Single(edge => edge.Scope == "Production").RelationshipCount.Should().Be(2);
        result.Edges.Single(edge => edge.Scope == "Test").RelationshipCount.Should().Be(3);
    }

    [Fact]
    public void Apply_keeps_packages_outside_the_base_as_leaf_nodes()
    {
        var projection = CreateProjection(
            [Package("inside", "shop.orders.web"), Package("outside", "com.vendor.adapter")],
            [Edge("outside-edge", "inside", "outside", "Production", 1, ArchitectureRelationshipKind.MethodCall, 0)]);

        var result = ArchitectureGraphRollup.Apply(projection, "shop", 1);

        result.Nodes.Should().ContainSingle(node => node.Name == "com.vendor.adapter" && node.LeafComponentIds.Contains("outside"));
        result.Nodes.Should().ContainSingle(node => node.Name == "orders");
        result.Edges.Should().ContainSingle().Which.RelationshipCount.Should().Be(1);
    }

    [Fact]
    public void Apply_preserves_module_boundaries_and_unprojected_evidence()
    {
        var projection = new ArchitectureGraphProjection(
            [
                Package("a1", "shop.orders.web", "/repo/module-a"),
                Package("a2", "shop.orders.persistence", "/repo/module-a"),
                Package("b1", "shop.orders.api", "/repo/module-b")
            ],
            [
                Edge("module-a", "a1", "a2", "Production", 1, ArchitectureRelationshipKind.MethodCall, 0)
            ],
            [
                new ArchitectureGraphUnprojectedEvidence
                {
                    Evidence = Evidence(9),
                    Reason = ArchitectureGraphUnprojectedReason.AmbiguousEvidenceReference,
                    Explanation = "Ambiguous source occurrence."
                }
            ]);

        var result = ArchitectureGraphRollup.Apply(projection, "shop", 1);

        result.Nodes.Should().ContainSingle(node => node.Name == "orders" && node.LeafComponentIds.Contains("a1"))
            .Which.LeafComponentIds.Should().Contain("a2");
        result.Nodes.Should().ContainSingle(node =>
            node.Name == "orders"
            && node.ModulePath == "/repo/module-b"
            && node.LeafComponentIds.Contains("b1"));
        result.UnprojectedEvidence.Should().BeEquivalentTo(projection.UnprojectedEvidence);
    }

    private static ArchitectureGraphProjection CreateProjection(
        IEnumerable<ArchitectureGraphNode> nodes,
        IEnumerable<ArchitectureGraphEdge> edges) =>
        new(nodes, edges, []);

    private static ArchitectureGraphNode Package(string id, string name, string module = "/repo") =>
        new()
        {
            Id = id,
            Name = name,
            Kind = ArchitectureGraphNodeKind.CodeComponent,
            Language = "Java",
            ComponentKind = "Package",
            ModulePath = module
        };

    private static ArchitectureGraphEdge Edge(
        string id,
        string source,
        string target,
        string scope,
        int relationshipCount,
        ArchitectureRelationshipKind kind,
        int occurrence) =>
        new()
        {
            Id = id,
            SourceNodeId = source,
            TargetNodeId = target,
            Domain = ArchitectureGraphEdgeDomain.NativeSemantic,
            Scope = scope,
            RelationshipCount = relationshipCount,
            RelationshipKinds = [kind],
            Evidence = [Evidence(occurrence)]
        };

    private static ArchitectureGraphEvidenceReference Evidence(int occurrence) =>
        new()
        {
            Domain = ArchitectureGraphEvidenceDomain.SemanticRelationship,
            OccurrenceIndex = occurrence,
            EvidenceId = $"relationship-{occurrence}",
            ProviderId = "Java"
        };
}
