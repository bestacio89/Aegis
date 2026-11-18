using System.IO;
using System.Linq;
using Xunit;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;

namespace Aegis.Wpf.Tests;

public sealed class DependencyGraphViewModelTests
{
    [Fact]
    public void Update_PrefersCodeContainerProjection_AndKeepsProjectionIdentitiesSeparate()
    {
        var context = new ProjectArchitectureContext
        {
            Modules =
            [
                new ProjectModuleContext { Name = "Api", Path = Path.Combine(Path.GetTempPath(), "api") },
                new ProjectModuleContext { Name = "Domain", Path = Path.Combine(Path.GetTempPath(), "domain") }
            ],
            ArchitectureComponents =
            [
                new ArchitectureComponentContext
                {
                    Id = "container-api",
                    Name = "Api",
                    QualifiedName = "Example.Api",
                    ComponentKind = "Namespace",
                    ProductionSymbolCount = 3
                },
                new ArchitectureComponentContext
                {
                    Id = "container-domain",
                    Name = "Domain",
                    QualifiedName = "Example.Domain",
                    ComponentKind = "Namespace",
                    ProductionSymbolCount = 2
                }
            ],
            ArchitecturalComponents =
            [
                new ArchitecturalComponentContext
                {
                    Id = "logical-interface",
                    Name = "Interface",
                    Role = "Presentation",
                    ProductionSymbolCount = 3
                },
                new ArchitecturalComponentContext
                {
                    Id = "logical-core",
                    Name = "Core",
                    Role = "Domain",
                    ProductionSymbolCount = 2
                }
            ],
            ArchitecturalComponentDependencies =
            [
                new ArchitecturalComponentDependencyContext
                {
                    SourceComponentId = "logical-interface",
                    SourceComponentName = "Interface",
                    TargetComponentId = "logical-core",
                    TargetComponentName = "Core",
                    RelationshipCount = 4
                }
            ]
        };

        var viewModel = new DependencyGraphViewModel();
        viewModel.Update(context);

        Assert.Equal("Code containers", viewModel.GraphProjection);
        Assert.Equal(
            new[] { "container-api", "container-domain" },
            viewModel.GraphNodes.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Empty(viewModel.GraphEdges);

        viewModel.GraphProjection = "Architectural components";

        Assert.Equal(
            new[] { "logical-core", "logical-interface" },
            viewModel.GraphNodes.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Single(viewModel.GraphEdges);
        Assert.Equal(4, viewModel.TotalDependencies);

        viewModel.GraphProjection = "Code containers";

        Assert.Equal(
            new[] { "container-api", "container-domain" },
            viewModel.GraphNodes.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal("CODE CONTAINERS", viewModel.NodeCountLabel);
        Assert.Empty(viewModel.GraphEdges);
    }

    [Fact]
    public void Update_RetainsIsolatedBuildModules_AndNormalizesBuildRelationshipEndpoints()
    {
        var apiPath = Path.Combine(Path.GetTempPath(), "aegis-tests", "api");
        var domainPath = Path.Combine(Path.GetTempPath(), "aegis-tests", "domain");
        var isolatedPath = Path.Combine(Path.GetTempPath(), "aegis-tests", "worker");
        var context = new ProjectArchitectureContext
        {
            Modules =
            [
                new ProjectModuleContext { Name = "Api", Path = apiPath, Type = "Presentation" },
                new ProjectModuleContext { Name = "Domain", Path = domainPath, Type = "Domain" },
                new ProjectModuleContext { Name = "Worker", Path = isolatedPath, Type = "Infrastructure" }
            ],
            Dependencies =
            [
                new ArchitectureDependencyContext
                {
                    Source = "Api",
                    SourceLayer = "Presentation",
                    SourceProjectPath = apiPath,
                    Target = "Domain",
                    TargetLayer = "Domain",
                    TargetProjectPath = domainPath,
                    DependencyType = "ProjectReference"
                }
            ]
        };

        var viewModel = new DependencyGraphViewModel();
        viewModel.Update(context);

        Assert.Equal("Build modules", viewModel.GraphProjection);
        Assert.Equal(3, viewModel.GraphNodes.Count);
        Assert.Equal(1, viewModel.GraphEdges.Count);
        Assert.Contains(viewModel.GraphNodes, node => node.Name == "Worker");
        Assert.Contains(viewModel.GraphNodes, node => node.Id == Path.GetFullPath(apiPath));
        Assert.Contains(viewModel.GraphNodes, node => node.Id == Path.GetFullPath(domainPath));
    }

    [Fact]
    public void Update_Retains_isolated_code_containers_across_multiple_languages()
    {
        var context = new ProjectArchitectureContext
        {
            Modules =
            [
                new ProjectModuleContext { Name = "api", Path = Path.Combine(Path.GetTempPath(), "polyglot", "api"), Languages = ["C#"] },
                new ProjectModuleContext { Name = "frontend", Path = Path.Combine(Path.GetTempPath(), "polyglot", "frontend"), Languages = ["TypeScript"] },
                new ProjectModuleContext { Name = "worker", Path = Path.Combine(Path.GetTempPath(), "polyglot", "worker"), Languages = ["Java"] }
            ],
            ArchitectureComponents =
            [
                new ArchitectureComponentContext
                {
                    Id = "csharp-project:api",
                    Name = "Api",
                    QualifiedName = "Api",
                    Language = "C#",
                    ComponentKind = "Project",
                    ProductionSymbolCount = 4
                },
                new ArchitectureComponentContext
                {
                    Id = "typescript-module:frontend",
                    Name = "Frontend",
                    QualifiedName = "src",
                    Language = "TypeScript",
                    ComponentKind = "Module",
                    ProductionSymbolCount = 3
                },
                new ArchitectureComponentContext
                {
                    Id = "java-package:worker",
                    Name = "Worker",
                    QualifiedName = "com.sample.worker",
                    Language = "Java",
                    ComponentKind = "Package",
                    ProductionSymbolCount = 2
                }
            ],
            ArchitectureComponentDependencies =
            [
                new ArchitectureComponentDependencyContext
                {
                    SourceComponentId = "csharp-project:api",
                    SourceComponentName = "Api",
                    TargetComponentId = "typescript-module:frontend",
                    TargetComponentName = "Frontend",
                    RelationshipCount = 1
                }
            ],
            ArchitectureComponentInternalDependencies =
            [
                new ArchitectureComponentDependencyContext
                {
                    SourceComponentId = "java-package:worker",
                    SourceComponentName = "com.sample.worker",
                    TargetComponentId = "java-package:worker",
                    TargetComponentName = "com.sample.worker",
                    IsInternal = true,
                    Scope = "Production",
                    RelationshipCount = 7
                }
            ]
        };

        var viewModel = new DependencyGraphViewModel();
        viewModel.Update(context);

        Assert.Equal("Code containers", viewModel.GraphProjection);
        Assert.Equal(3, viewModel.GraphNodes.Count);
        Assert.Equal(1, viewModel.GraphEdges.Count);
        Assert.DoesNotContain(viewModel.GraphEdges, edge =>
            edge.Source == "com.sample.worker" && edge.Target == "com.sample.worker");
        Assert.Contains(viewModel.GraphNodes, node => node.Id == "csharp-project:api");
        Assert.Contains(viewModel.GraphNodes, node => node.Id == "typescript-module:frontend");
        Assert.Contains(viewModel.GraphNodes, node => node.Id == "java-package:worker");
    }

    [Fact]
    public void SelectingNode_RestrictsGraphToItsAdjacentRelationships()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureComponents =
            [
                Container("a", "A"),
                Container("b", "B"),
                Container("c", "C")
            ],
            ArchitectureComponentDependencies =
            [
                Edge("a", "b"),
                Edge("b", "c")
            ]
        };

        var viewModel = new DependencyGraphViewModel();
        viewModel.Update(context);
        viewModel.GraphProjection = "Code containers";

        var selected = Assert.Single(viewModel.GraphNodes.Where(node => node.Id == "b"));
        viewModel.SelectGraphNodeCommand.Execute(selected);

        Assert.Equal("b", viewModel.SelectedGraphNode?.Id);
        Assert.Equal(2, viewModel.GraphEdges.Count);
        Assert.Equal(2, viewModel.FilteredDependencies);
    }


    [Fact]
    public void Update_uses_architectural_roles_for_code_container_nodes_and_relationship_endpoints()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureComponents =
            [
                new ArchitectureComponentContext { Id = "package:domain", Name = "domain", QualifiedName = "sample.domain", ComponentKind = "Package", ProductionSymbolCount = 2 },
                new ArchitectureComponentContext { Id = "package:application", Name = "application", QualifiedName = "sample.application", ComponentKind = "Package", ProductionSymbolCount = 2 },
                new ArchitectureComponentContext { Id = "package:unknown", Name = "unknown", QualifiedName = "sample.unknown", ComponentKind = "Package", ProductionSymbolCount = 1 }
            ],
            ArchitecturalComponents =
            [
                new ArchitecturalComponentContext { Id = "logical-domain", Name = "Domain", Role = "Domain", ProductionSymbolCount = 2 },
                new ArchitecturalComponentContext { Id = "logical-business", Name = "Business", Role = "Business", ProductionSymbolCount = 2 }
            ],
            ArchitecturalComponentMemberships =
            [
                new ArchitecturalComponentMembership { CodeComponentId = "package:domain", ArchitecturalComponentId = "logical-domain", ClassificationSource = Aegis.Shared.Architecture.Enums.ArchitectureClassificationSource.Convention },
                new ArchitecturalComponentMembership { CodeComponentId = "package:application", ArchitecturalComponentId = "logical-business", ClassificationSource = Aegis.Shared.Architecture.Enums.ArchitectureClassificationSource.Convention }
            ],
            ArchitectureComponentDependencies =
            [
                new ArchitectureComponentDependencyContext
                {
                    SourceComponentId = "package:application",
                    SourceComponentName = "sample.application",
                    TargetComponentId = "package:domain",
                    TargetComponentName = "sample.domain",
                    RelationshipCount = 3
                },
                new ArchitectureComponentDependencyContext
                {
                    SourceComponentId = "package:unknown",
                    SourceComponentName = "sample.unknown",
                    TargetComponentId = "package:domain",
                    TargetComponentName = "sample.domain",
                    RelationshipCount = 1
                }
            ]
        };

        var viewModel = new DependencyGraphViewModel();
        viewModel.Update(context);

        viewModel.GraphNodes.Should().HaveCount(3);
        viewModel.GraphNodes.Should().ContainSingle(node => node.Id == "package:domain" && node.Layer == "Domain");
        viewModel.GraphNodes.Should().ContainSingle(node => node.Id == "package:application" && node.Layer == "Business");
        viewModel.GraphNodes.Should().ContainSingle(node => node.Id == "package:unknown" && node.Layer == "Package");
        viewModel.GraphEdges.Should().HaveCount(2);
        viewModel.GraphEdges.Should().Contain(edge =>
            edge.Source == "sample.application" && edge.Target == "sample.domain"
            && edge.SourceLayer == "Business" && edge.TargetLayer == "Domain");
        viewModel.GraphEdges.Should().Contain(edge =>
            edge.Source == "sample.unknown" && edge.SourceLayer == "Package"
            && edge.TargetLayer == "Domain");
    }

    private static ArchitectureComponentContext Container(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            QualifiedName = name,
            ComponentKind = "Package",
            ProductionSymbolCount = 1
        };

    private static ArchitectureComponentDependencyContext Edge(string source, string target) =>
        new()
        {
            SourceComponentId = source,
            SourceComponentName = source.ToUpperInvariant(),
            TargetComponentId = target,
            TargetComponentName = target.ToUpperInvariant(),
            RelationshipCount = 1
        };
}
