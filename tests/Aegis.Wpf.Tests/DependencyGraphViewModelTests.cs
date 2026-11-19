using System.Threading;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;
using Xunit;

namespace Aegis.Wpf.Tests;

public sealed class DependencyGraphViewModelTests
{
    [Fact]
    public void Update_uses_unified_projection_and_keeps_graph_edges_identical_across_node_modes()
    {
        var projection = new ArchitectureGraphProjection(
            [
                Code("code:api", "Example.Api", "Namespace"),
                Code("code:domain", "Example.Domain", "Namespace"),
                Logical("logical:api", "Api", "Presentation"),
                Logical("logical:domain", "Domain", "Domain")
            ],
            [
                Edge("code-edge", "code:api", "code:domain"),
                Edge("logical-edge", "logical:api", "logical:domain")
            ],
            []);
        var viewModel = new DependencyGraphViewModel();
        viewModel.Update(new ProjectArchitectureContext { ArchitectureGraphProjection = projection });

        Assert.Equal("Code containers", viewModel.GraphProjection);
        Assert.Equal(2, viewModel.GraphEdges.Count);
        Assert.Equal(2, viewModel.TotalDependencies);

        viewModel.GraphProjection = "Architectural components";

        Assert.Equal("ARCHITECTURAL COMPONENTS", viewModel.NodeCountLabel);
        Assert.Equal(2, viewModel.GraphEdges.Count);
        Assert.Equal(2, viewModel.TotalDependencies);
    }

    [Fact]
    public void Update_retains_isolated_build_modules_and_uses_only_projected_build_edges()
    {
        var projection = new ArchitectureGraphProjection(
            [
                Module("module:api", "Api", "/repo/api", "Presentation"),
                Module("module:domain", "Domain", "/repo/domain", "Domain"),
                Module("module:worker", "Worker", "/repo/worker", "Infrastructure")
            ],
            [Edge("project-reference", "module:api", "module:domain", ArchitectureGraphEdgeDomain.Build,
                ArchitectureRelationshipKind.BuildProjectReference)],
            []);
        var viewModel = new DependencyGraphViewModel();
        viewModel.Update(new ProjectArchitectureContext { ArchitectureGraphProjection = projection });

        Assert.Equal("Build modules", viewModel.GraphProjection);
        Assert.Equal(3, viewModel.GraphNodes.Count);
        Assert.Single(viewModel.GraphEdges);
        Assert.Contains(viewModel.GraphNodes, node => node.Name == "Worker");
        Assert.Contains(viewModel.GraphNodes, node => node.Name == "Api");
        Assert.Contains(viewModel.GraphNodes, node => node.Name == "Domain");
    }

    [Fact]
    public void Update_retains_polyglot_leaf_nodes_and_collapses_same_package_relationships_to_internal_counts()
    {
        var projection = new ArchitectureGraphProjection(
            [
                Code("csharp", "Api", "Project", "C#"),
                Code("typescript", "src", "Module", "TypeScript"),
                Code("java-web", "com.sample.orders.web", "Package", "Java", "/repo/java"),
                Code("java-persistence", "com.sample.orders.persistence", "Package", "Java", "/repo/java"),
                Code("java-billing", "com.sample.billing.api", "Package", "Java", "/repo/java")
            ],
            [
                Edge("cross-language", "csharp", "typescript"),
                Edge("same-package-group", "java-web", "java-persistence")
            ],
            []);
        var viewModel = new DependencyGraphViewModel();
        viewModel.Update(new ProjectArchitectureContext { ArchitectureGraphProjection = projection });

        Assert.Equal(4, viewModel.GraphNodes.Count);
        Assert.Single(viewModel.GraphEdges);
        Assert.Contains(viewModel.GraphNodes, node => node.Name == "Api");
        Assert.Contains(viewModel.GraphNodes, node => node.Name == "src");
        Assert.Contains(viewModel.GraphNodes, node => node.Name == "orders");
        Assert.Equal(1, viewModel.TotalDependencies);
    }

    [Fact]
    public void SelectingNode_restricts_graph_to_its_adjacent_rolled_up_relationships()
    {
        var projection = new ArchitectureGraphProjection(
            [Code("a", "A", "Package"), Code("b", "B", "Package"), Code("c", "C", "Package")],
            [Edge("a-b", "a", "b"), Edge("b-c", "b", "c")],
            []);
        var viewModel = new DependencyGraphViewModel();
        viewModel.Update(new ProjectArchitectureContext { ArchitectureGraphProjection = projection });
        var selected = Assert.Single(viewModel.GraphNodes.Where(node => node.Name == "B"));
        viewModel.SelectGraphNodeCommand.Execute(selected);

        Assert.Equal(selected.Id, viewModel.SelectedGraphNode?.Id);
        Assert.Equal(2, viewModel.GraphEdges.Count);
        Assert.Equal(2, viewModel.FilteredDependencies);
    }

    [Fact]
    public void Update_uses_architectural_roles_from_graph_nodes_without_rebuilding_membership_maps()
    {
        var projection = new ArchitectureGraphProjection(
            [
                Code("domain", "sample.domain", "Package", "Java", role: "Domain"),
                Code("application", "sample.application", "Package", "Java", role: "Business"),
                Code("unknown", "sample.unknown", "Package", "Java")
            ],
            [Edge("application-domain", "application", "domain"), Edge("unknown-domain", "unknown", "domain")],
            []);
        var viewModel = new DependencyGraphViewModel();
        viewModel.Update(new ProjectArchitectureContext { ArchitectureGraphProjection = projection });

        Assert.Contains(viewModel.GraphNodes, node => node.Name == "domain" && node.Layer == "Domain");
        Assert.Contains(viewModel.GraphNodes, node => node.Name == "application" && node.Layer == "Business");
        Assert.Contains(viewModel.GraphNodes, node => node.Name == "unknown" && node.Layer == "Package");
        Assert.Equal(2, viewModel.GraphEdges.Count);
    }

    [Fact]
    public void Depth_selector_updates_shared_rollup_state()
    {
        RunOnStaThread(() =>
        {
            var options = new ArchitectureGraphViewOptions();
            var graph = new DependencyGraphViewModel(options);
            var ledger = new RelationshipLedgerViewModel(options);
            var landscape = new SystemLandscapeViewModel(options);

            graph.RollupDepth = 2;

            Assert.Equal(2, ledger.RollupDepth);
            Assert.Equal(2, landscape.RollupDepth);
        });
    }

    [Fact]
    public void Context_resolves_module_package_bases_identically_in_graph_ledger_and_landscape()
    {
        RunOnStaThread(() =>
        {
            var projection = new ArchitectureGraphProjection(
                [
                    Code("orders-web", "com.acme.orders.web", "Package", "Java", "/repo/orders"),
                    Code("orders-persistence", "com.acme.orders.persistence", "Package", "Java", "/repo/orders"),
                    Code("orders-billing", "com.vendor.billing.api", "Package", "Java", "/repo/orders"),
                    Code("sales-web", "com.other.sales.web", "Package", "Java", "/repo/sales"),
                    Code("sales-persistence", "com.other.sales.persistence", "Package", "Java", "/repo/sales"),
                    Code("sales-billing", "com.vendor.billing.api", "Package", "Java", "/repo/sales")
                ],
                [
                    Edge("orders-internal", "orders-web", "orders-persistence"),
                    Edge("orders-external", "orders-web", "orders-billing"),
                    Edge("sales-internal", "sales-web", "sales-persistence"),
                    Edge("sales-external", "sales-web", "sales-billing")
                ],
                []);
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
            var options = new ArchitectureGraphViewOptions();
            var graph = new DependencyGraphViewModel(options);
            var ledger = new RelationshipLedgerViewModel(options);
            var landscape = new SystemLandscapeViewModel(options);

            graph.Update(context);
            ledger.Update(context);
            landscape.Update(context);

            const string expectedLabel = "Base package: resolved per module/language partition (2)";
            Assert.Equal(expectedLabel, graph.BasePackageLabel);
            Assert.Equal(expectedLabel, ledger.BasePackageLabel);
            Assert.Equal(expectedLabel, landscape.BasePackageLabel);

            var expectedRelationships = new[] { "orders->com.vendor.billing.api", "sales->com.vendor.billing.api" };
            Assert.Equal(
                expectedRelationships,
                graph.GraphEdges.Select(edge => $"{edge.Source}->{edge.Target}").OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(
                expectedRelationships,
                ledger.Dependencies.Select(edge => $"{edge.Source}->{edge.Target}").OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(
                expectedRelationships,
                landscape.Relationships
                    .Where(edge => !edge.IsInternalToGroup)
                    .Select(edge => $"{edge.Source}->{edge.Target}")
                    .OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(
                new[] { "orders-internal", "sales-internal" },
                landscape.Relationships
                    .Where(edge => edge.IsInternalToGroup)
                    .Select(edge => edge.EdgeId)
                    .OrderBy(value => value, StringComparer.Ordinal));
            Assert.Equal(
                new[] { "com.vendor.billing.api", "com.vendor.billing.api", "orders", "sales" },
                graph.GraphNodes.Select(node => node.Name).OrderBy(value => value, StringComparer.Ordinal));
        });
    }

    [Fact]
    public void Shared_depth_produces_identical_relationship_sets_in_graph_ledger_and_landscape()
    {
        RunOnStaThread(() =>
        {
            var projection = new ArchitectureGraphProjection(
                [
                    Code("web", "shop.orders.web", "Package", "Java", "/repo"),
                    Code("persistence", "shop.orders.persistence", "Package", "Java", "/repo"),
                    Code("billing", "shop.billing.api", "Package", "Java", "/repo")
                ],
                [
                    Edge("internal", "web", "persistence"),
                    Edge("external", "web", "billing")
                ],
                []);
            var context = new ProjectArchitectureContext { ArchitectureGraphProjection = projection };
            var options = new ArchitectureGraphViewOptions();
            var graph = new DependencyGraphViewModel(options);
            var ledger = new RelationshipLedgerViewModel(options);
            var landscape = new SystemLandscapeViewModel(options);

            graph.Update(context);
            ledger.Update(context);
            landscape.Update(context);

            Assert.Single(graph.GraphEdges);
            Assert.Single(ledger.Dependencies);
            Assert.Single(landscape.Relationships.Where(edge => !edge.IsInternalToGroup));
            Assert.Single(landscape.Relationships.Where(edge => edge.IsInternalToGroup));

            options.Depth = 2;

            Assert.Equal(2, graph.GraphEdges.Count);
            Assert.Equal(2, ledger.Dependencies.Count);
            Assert.Equal(2, landscape.Relationships.Count);
            Assert.All(landscape.Relationships, edge => Assert.False(edge.IsInternalToGroup));
            Assert.Equal(
                graph.GraphEdges.Select(edge => $"{edge.Source}->{edge.Target}").OrderBy(value => value, StringComparer.Ordinal),
                landscape.Relationships.Select(edge => $"{edge.Source}->{edge.Target}").OrderBy(value => value, StringComparer.Ordinal));
        });
    }

    [Fact]
    public void Graph_ledger_and_landscape_preserve_distinct_scope_edges_with_identical_edge_ids()
    {
        RunOnStaThread(() =>
        {
            var projection = new ArchitectureGraphProjection(
                [Module("module:a", "A", "/repo/a", "Service"), Module("module:b", "B", "/repo/b", "Service")],
                [
                    Edge("production-edge", "module:a", "module:b", ArchitectureGraphEdgeDomain.Build, ArchitectureRelationshipKind.BuildProjectReference),
                    new ArchitectureGraphEdge
                    {
                        Id = "test-edge", SourceNodeId = "module:a", TargetNodeId = "module:b",
                        Domain = ArchitectureGraphEdgeDomain.Build, Scope = "Test", RelationshipCount = 2,
                        RelationshipKinds = [ArchitectureRelationshipKind.BuildModuleDependency]
                    }
                ],
                []);
            var context = new ProjectArchitectureContext { ArchitectureGraphProjection = projection };
            var options = new ArchitectureGraphViewOptions();
            var graph = new DependencyGraphViewModel(options);
            var ledger = new RelationshipLedgerViewModel(options);
            var landscape = new SystemLandscapeViewModel(options);

            graph.Update(context);
            ledger.Update(context);
            landscape.Update(context);

            var graphIds = graph.GraphEdges.Select(edge => edge.GraphEdgeId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var ledgerIds = ledger.Dependencies.Select(edge => edge.GraphEdgeId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var landscapeIds = landscape.Relationships.Select(edge => edge.EdgeId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Assert.Equal(2, graphIds.Length);
            Assert.Equal(ledgerIds, graphIds);
            Assert.Equal(landscapeIds, graphIds);
        });
    }

    private static ArchitectureComponentContext Component(string id, string qualifiedName) =>
        new()
        {
            Id = id,
            Name = qualifiedName.Split('.').Last(),
            QualifiedName = qualifiedName,
            Language = "Java",
            ComponentKind = "Package"
        };

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

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new AggregateException("The dependency graph ViewModel test failed on its STA thread.", failure);
    }

    private static ArchitectureGraphNode Code(
        string id, string name, string kind, string? language = "C#", string? module = "/repo",
        string? role = null) =>
        new()
        {
            Id = id, Name = name, Kind = ArchitectureGraphNodeKind.CodeComponent,
            ComponentKind = kind, Language = language, ModulePath = module, ArchitecturalRole = role
        };

    private static ArchitectureGraphNode Logical(string id, string name, string role) =>
        new() { Id = id, Name = name, Kind = ArchitectureGraphNodeKind.LogicalComponent, ComponentKind = role };

    private static ArchitectureGraphNode Module(string id, string name, string path, string role) =>
        new() { Id = id, Name = name, Kind = ArchitectureGraphNodeKind.BuildModule, ModulePath = path, ComponentKind = role };

    private static ArchitectureGraphEdge Edge(
        string id, string source, string target,
        ArchitectureGraphEdgeDomain domain = ArchitectureGraphEdgeDomain.NativeSemantic,
        ArchitectureRelationshipKind kind = ArchitectureRelationshipKind.Calls) =>
        new()
        {
            Id = id, SourceNodeId = source, TargetNodeId = target, Domain = domain,
            Scope = "Production", RelationshipCount = 1, RelationshipKinds = [kind]
        };
}
