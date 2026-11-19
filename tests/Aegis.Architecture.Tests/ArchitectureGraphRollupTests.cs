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
                Edge("orders-internal", "web", "persistence", "Production", 1, ArchitectureRelationshipKind.Calls, 0),
                Edge("orders-billing-a", "web", "billing", "Production", 2, ArchitectureRelationshipKind.Calls, 1),
                Edge("orders-billing-b", "persistence", "billing", "Production", 3, ArchitectureRelationshipKind.References, 2)
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
            [ArchitectureRelationshipKind.Calls, ArchitectureRelationshipKind.References]);
        edge.Evidence.Should().HaveCount(2);
        edge.EvidenceFiles.Should().BeEquivalentTo(["/repo/src/File1.java", "/repo/src/File2.java"]);
        edge.LeafEdgeIds.Should().BeEquivalentTo(["orders-billing-a", "orders-billing-b"]);
    }

    [Fact]
    public void Apply_layer_first_packages_groups_by_first_qualified_name_segment_without_explicit_base()
    {
        var projection = CreateProjection(
            [Package("controller", "controller.admin"), Package("service", "service.impl")],
            [Edge("controller-service", "controller", "service", "Production", 1, ArchitectureRelationshipKind.Calls, 0)]);

        var result = ArchitectureGraphRollup.Apply(projection, null, 1);

        result.Nodes.Select(node => node.Name).Should().BeEquivalentTo(["controller", "service"]);
        result.Edges.Should().ContainSingle().Which.RelationshipCount.Should().Be(1);
    }

    [Fact]
    public void Apply_context_resolves_spring_boot_package_base_per_module_and_labels_distinct_partitions()
    {
        var projection = CreateProjection(
            [
                Package("orders-web", "com.acme.orders.web", "/repo/orders"),
                Package("orders-persistence", "com.acme.orders.persistence", "/repo/orders"),
                Package("orders-billing", "com.vendor.billing.api", "/repo/orders"),
                Package("sales-web", "com.other.sales.web", "/repo/sales"),
                Package("sales-persistence", "com.other.sales.persistence", "/repo/sales"),
                Package("sales-billing", "com.vendor.billing.api", "/repo/sales")
            ],
            [
                Edge("orders-internal", "orders-web", "orders-persistence", "Production", 1, ArchitectureRelationshipKind.Calls, 0),
                Edge("orders-external", "orders-web", "orders-billing", "Production", 2, ArchitectureRelationshipKind.Calls, 1),
                Edge("sales-internal", "sales-web", "sales-persistence", "Production", 1, ArchitectureRelationshipKind.Calls, 2),
                Edge("sales-external", "sales-web", "sales-billing", "Production", 3, ArchitectureRelationshipKind.Calls, 3)
            ]);
        var context = new ProjectArchitectureContext
        {
            ArchitectureGraphProjection = projection,
            ArchitectureComponents =
            [
                Component("orders-web", "com.acme.orders.web"),
                Component("orders-persistence", "com.acme.orders.persistence"),
                Component("orders-billing", "com.vendor.billing.api"),
                Component("sales-web", "com.other.sales.web"),
                Component("sales-persistence", "com.other.sales.persistence"),
                Component("sales-billing", "com.vendor.billing.api")
            ],
            SemanticAnalysisRuns =
            [
                SpringBootRun("orders-run", "/repo/orders", "com.acme"),
                SpringBootRun("sales-run", "/repo/sales", "com.other")
            ]
        };

        var result = ArchitectureGraphRollup.Apply(context, null, 1);

        result.BasePackage.Should().BeEmpty();
        result.BasePackageLabel.Should().Be("Base package: resolved per module/language partition (2)");
        result.Nodes.Should().ContainSingle(node =>
            node.Name == "orders"
            && node.ModulePath == "/repo/orders"
            && node.LeafComponentIds.Contains("orders-web")
            && node.LeafComponentIds.Contains("orders-persistence"));
        result.Nodes.Should().ContainSingle(node =>
            node.Name == "sales"
            && node.ModulePath == "/repo/sales"
            && node.LeafComponentIds.Contains("sales-web")
            && node.LeafComponentIds.Contains("sales-persistence"));
        result.Nodes.Should().ContainSingle(node =>
            node.Name == "com.vendor.billing.api"
            && node.ModulePath == "/repo/orders"
            && node.LeafComponentIds.Contains("orders-billing"));
        result.Nodes.Should().ContainSingle(node =>
            node.Name == "com.vendor.billing.api"
            && node.ModulePath == "/repo/sales"
            && node.LeafComponentIds.Contains("sales-billing"));
        result.Nodes.Single(node => node.Name == "orders").InternalRelationshipCounts["Production"].Should().Be(1);
        result.Nodes.Single(node => node.Name == "sales").InternalRelationshipCounts["Production"].Should().Be(1);
        result.Edges.Should().HaveCount(2);
        result.Edges.Select(edge => edge.RelationshipCount).Should().BeEquivalentTo([2, 3]);
    }

    [Fact]
    public void Apply_resolves_package_roots_independently_by_language_and_module()
    {
        var projection = CreateProjection(
            [
                new ArchitectureGraphNode
                {
                    Id = "csharp", Name = "Api", Kind = ArchitectureGraphNodeKind.CodeComponent,
                    ComponentKind = "Project", Language = "C#", ModulePath = "/repo/csharp"
                },
                new ArchitectureGraphNode
                {
                    Id = "typescript", Name = "src", Kind = ArchitectureGraphNodeKind.CodeComponent,
                    ComponentKind = "Module", Language = "TypeScript", ModulePath = "/repo/typescript"
                },
                Package("java-web", "com.sample.orders.web", "/repo/java"),
                Package("java-persistence", "com.sample.orders.persistence", "/repo/java"),
                Package("java-billing", "com.sample.billing.api", "/repo/java"),
                Package("java-other-web", "org.vendor.sales.web", "/repo/java-other"),
                Package("java-other-persistence", "org.vendor.sales.persistence", "/repo/java-other"),
                Package("java-other-billing", "org.vendor.billing.api", "/repo/java-other")
            ],
            [
                Edge("orders-internal", "java-web", "java-persistence", "Production", 1, ArchitectureRelationshipKind.Calls, 0),
                Edge("sales-internal", "java-other-web", "java-other-persistence", "Production", 1, ArchitectureRelationshipKind.Calls, 1)
            ]);

        var result = ArchitectureGraphRollup.Apply(projection, null, 1);

        result.Nodes.Should().HaveCount(6);
        result.Nodes.Should().ContainSingle(node => node.Name == "Api" && node.LeafComponentIds.Contains("csharp"));
        result.Nodes.Should().ContainSingle(node => node.Name == "src" && node.LeafComponentIds.Contains("typescript"));
        result.Nodes.Should().ContainSingle(node => node.Name == "orders"
            && node.ModulePath == "/repo/java"
            && node.LeafComponentIds.Contains("java-web")
            && node.LeafComponentIds.Contains("java-persistence"));
        result.Nodes.Should().ContainSingle(node => node.Name == "billing"
            && node.ModulePath == "/repo/java"
            && node.LeafComponentIds.Contains("java-billing"));
        result.Nodes.Should().ContainSingle(node => node.Name == "billing"
            && node.ModulePath == "/repo/java-other"
            && node.LeafComponentIds.Contains("java-other-billing"));
        result.Nodes.Should().ContainSingle(node => node.Name == "sales"
            && node.ModulePath == "/repo/java-other"
            && node.LeafComponentIds.Contains("java-other-web")
            && node.LeafComponentIds.Contains("java-other-persistence"));
        result.Nodes.Single(node => node.Name == "orders").InternalRelationshipCounts["Production"].Should().Be(1);
        result.Nodes.Single(node => node.Name == "sales").InternalRelationshipCounts["Production"].Should().Be(1);
    }

    [Fact]
    public void Apply_resolves_partition_base_from_production_packages_and_preserves_test_packages()
    {
        var projection = CreateProjection(
            [
                Package("orders-web", "com.acme.orders.web"),
                Package("orders-persistence", "com.acme.orders.persistence"),
                Package("billing-api", "com.acme.billing.api"),
                Package("test-fixture", "org.tests.fixture") with { IsTestScoped = true }
            ],
            []);

        var result = ArchitectureGraphRollup.Apply(
            new ProjectArchitectureContext { ArchitectureGraphProjection = projection },
            null,
            1);

        result.BasePackage.Should().Be("com.acme");
        result.Nodes.Should().ContainSingle(node => node.Name == "orders")
            .Which.LeafComponentIds.Should().BeEquivalentTo(["orders-web", "orders-persistence"]);
        result.Nodes.Should().ContainSingle(node => node.Name == "billing")
            .Which.LeafComponentIds.Should().Contain("billing-api");
        result.Nodes.Should().ContainSingle(node =>
            node.Name == "org.tests.fixture"
            && node.LeafComponentIds.Contains("test-fixture"));
        result.Nodes.Should().HaveCount(3);
    }

    [Fact]
    public void Apply_falls_back_to_test_packages_when_partition_has_no_production_packages()
    {
        var projection = CreateProjection(
            [
                Package("test-web", "org.tests.orders.web") with { IsTestScoped = true },
                Package("test-persistence", "org.tests.orders.persistence") with { IsTestScoped = true },
                Package("test-billing", "org.tests.billing.api") with { IsTestScoped = true }
            ],
            []);

        var result = ArchitectureGraphRollup.Apply(
            new ProjectArchitectureContext { ArchitectureGraphProjection = projection },
            null,
            1);

        result.BasePackage.Should().Be("org.tests");
        result.Nodes.Should().ContainSingle(node => node.Name == "orders")
            .Which.LeafComponentIds.Should().BeEquivalentTo(["test-web", "test-persistence"]);
        result.Nodes.Should().ContainSingle(node => node.Name == "billing")
            .Which.LeafComponentIds.Should().Contain("test-billing");
    }

    [Fact]
    public void Resolve_context_excludes_test_only_packages_when_production_packages_exist()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureComponents =
            [
                new ArchitectureComponentContext
                {
                    Id = "production-web",
                    QualifiedName = "com.acme.orders.web",
                    ComponentKind = "Package",
                    ProductionSymbolCount = 4
                },
                new ArchitectureComponentContext
                {
                    Id = "production-persistence",
                    QualifiedName = "com.acme.orders.persistence",
                    ComponentKind = "Package",
                    ProductionSymbolCount = 2
                },
                new ArchitectureComponentContext
                {
                    Id = "production-billing",
                    QualifiedName = "com.acme.billing.api",
                    ComponentKind = "Package",
                    ProductionSymbolCount = 1
                },
                new ArchitectureComponentContext
                {
                    Id = "test-fixture",
                    QualifiedName = "org.tests.fixture",
                    ComponentKind = "Package",
                    TestSymbolCount = 3
                }
            ]
        };

        ArchitecturePackageBaseResolver.Resolve(context).Should().Be("com.acme");
    }

    [Fact]
    public void Apply_keeps_production_and_test_relationships_in_separate_aggregates()
    {
        var projection = CreateProjection(
            [Package("a", "shop.orders.web"), Package("b", "shop.billing.api")],
            [
                Edge("production", "a", "b", "Production", 2, ArchitectureRelationshipKind.Calls, 0),
                Edge("test", "a", "b", "Test", 3, ArchitectureRelationshipKind.References, 1)
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
            [Edge("outside-edge", "inside", "outside", "Production", 1, ArchitectureRelationshipKind.Calls, 0)]);

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
                Edge("module-a", "a1", "a2", "Production", 1, ArchitectureRelationshipKind.Calls, 0)
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

    [Fact]
    public void Apply_keeps_unresolved_edges_separate_from_projected_edges()
    {
        var projection = CreateProjection(
            [Package("known", "shop.orders.web")],
            [Edge("missing-endpoint", "known", "missing", "Production", 4, ArchitectureRelationshipKind.Calls, 0)]);

        var result = ArchitectureGraphRollup.Apply(projection, "shop", 1);

        result.Edges.Should().BeEmpty();
        result.UnresolvedEdges.Should().ContainSingle()
            .Which.RelationshipCount.Should().Be(4);
    }

    [Fact]
    public void Resolve_prefers_policy_override_then_application_entry_package_then_branch_fallback()
    {
        var names = new[] { "com.example.orders.web", "com.example.orders.persistence", "com.example.billing.api" };

        ArchitecturePackageBaseResolver.Resolve(names, "org.override", "com.example")
            .Should().Be("org.override");
        ArchitecturePackageBaseResolver.Resolve(names, null, "com.example")
            .Should().Be("com.example");
        ArchitecturePackageBaseResolver.Resolve(names)
            .Should().Be("com.example");
    }

    [Fact]
    public void Resolve_uses_unique_spring_boot_application_annotation_package()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureComponents =
            [
                Component("orders", "com.example.orders.web"),
                Component("billing", "com.example.billing.api")
            ],
            SemanticAnalysisRuns =
            [
                new SemanticAnalysisRun
                {
                    RunId = "java-run",
                    Language = "Java",
                    Symbols =
                    [
                        new SemanticSymbol { Id = "application", Name = "Application", QualifiedName = "com.example.app.Application", ContainerName = "com.example.app" },
                        new SemanticSymbol { Id = "annotation", Name = "SpringBootApplication", QualifiedName = "org.springframework.boot.autoconfigure.SpringBootApplication" }
                    ],
                    Relationships =
                    [
                        new SemanticRelationship
                        {
                            Id = "annotation-use",
                            SourceSymbolId = "application",
                            TargetSymbolId = "annotation",
                            Kind = SemanticRelationshipKind.UsesAnnotation
                        }
                    ]
                }
            ]
        };

        ArchitecturePackageBaseResolver.Resolve(context).Should().Be("com.example.app");
    }

    [Fact]
    public void Apply_rolls_up_python_modules_using_the_same_qualified_name_segments()
    {
        var projection = CreateProjection(
            [
                new ArchitectureGraphNode { Id = "web", Name = "shop.orders.web", Kind = ArchitectureGraphNodeKind.CodeComponent, ComponentKind = "Module", Language = "Python", ModulePath = "/repo" },
                new ArchitectureGraphNode { Id = "persistence", Name = "shop.orders.persistence", Kind = ArchitectureGraphNodeKind.CodeComponent, ComponentKind = "Module", Language = "Python", ModulePath = "/repo" },
                new ArchitectureGraphNode { Id = "billing", Name = "shop.billing.api", Kind = ArchitectureGraphNodeKind.CodeComponent, ComponentKind = "Module", Language = "Python", ModulePath = "/repo" }
            ],
            [Edge("internal", "web", "persistence", "Production", 1, ArchitectureRelationshipKind.Calls, 0)]);

        var result = ArchitectureGraphRollup.Apply(projection, "shop", 1);

        result.Nodes.Should().ContainSingle(node => node.Name == "orders")
            .Which.LeafComponentIds.Should().BeEquivalentTo(["web", "persistence"]);
        result.Nodes.Should().ContainSingle(node => node.Name == "billing");
        result.Nodes.Single(node => node.Name == "orders").InternalRelationshipCounts["Production"].Should().Be(1);
    }

    private static SemanticAnalysisRun SpringBootRun(
        string runId,
        string projectPath,
        string applicationPackage) =>
        new()
        {
            RunId = runId,
            Language = "Java",
            ProjectPath = projectPath,
            Symbols =
            [
                new SemanticSymbol
                {
                    Id = "application",
                    Name = "Application",
                    QualifiedName = applicationPackage + ".Application",
                    ContainerName = applicationPackage
                },
                new SemanticSymbol
                {
                    Id = "spring-annotation",
                    Name = "SpringBootApplication",
                    QualifiedName = "org.springframework.boot.autoconfigure.SpringBootApplication"
                }
            ],
            Relationships =
            [
                new SemanticRelationship
                {
                    Id = "application-annotation",
                    SourceSymbolId = "application",
                    TargetSymbolId = "spring-annotation",
                    Kind = SemanticRelationshipKind.UsesAnnotation
                }
            ]
        };

    private static ArchitectureComponentContext Component(string id, string qualifiedName) =>
        new()
        {
            Id = id,
            Name = qualifiedName.Split('.').Last(),
            QualifiedName = qualifiedName,
            Language = "Java",
            ComponentKind = "Package"
        };

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
            ProviderId = "Java",
            FilePaths = [$"/repo/src/File{occurrence}.java"]
        };
}
