using System.IO;
using System.Linq;
using Xunit;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;

namespace Aegis.Wpf.Tests;

public sealed class DependencyGraphViewModelTests
{
    [Fact]
    public void Update_PrefersLogicalProjection_AndKeepsProjectionIdentitiesSeparate()
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

        Assert.Equal("Architectural components", viewModel.GraphProjection);
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
