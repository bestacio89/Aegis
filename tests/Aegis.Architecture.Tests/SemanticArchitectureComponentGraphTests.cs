using Aegis.Architecture.Analysis.Semantics;
using Aegis.Architecture.Analysis.Detectors;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class SemanticArchitectureComponentGraphTests
{
    [Fact]
    public async Task Projects_package_graph_from_partial_jdt_runs_even_when_module_inventory_contains_duplicate_paths()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), $"aegis-java-graph-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(projectPath, "src", "main", "java");
        var applicationPackage = Path.Combine(sourceRoot, "sample", "application");
        var domainPackage = Path.Combine(sourceRoot, "sample", "domain");
        Directory.CreateDirectory(applicationPackage);
        Directory.CreateDirectory(domainPackage);

        var applicationFile = Path.Combine(applicationPackage, "OrderService.java");
        var domainFile = Path.Combine(domainPackage, "Order.java");
        await File.WriteAllTextAsync(applicationFile, "package sample.application; public class OrderService {}");
        await File.WriteAllTextAsync(domainFile, "package sample.domain; public record Order(String id) {}");

        try
        {
            var module = new ProjectModuleContext
            {
                Name = "Template",
                Path = projectPath,
                Languages = ["Java"],
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = sourceRoot,
                        Ownership = ProjectSourceRootOwnership.Production,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    }
                ]
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "Template",
                RootPath = projectPath,
                Language = "Java",
                Modules = [module, new ProjectModuleContext
                {
                    Name = "Template duplicate",
                    Path = projectPath,
                    Languages = ["Java"],
                    SourceRoots = module.SourceRoots
                }]
            };

            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [new PartialPackageGraphAdapter(applicationFile, domainFile)],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);

            var pipeline = new SemanticAnalysisPipeline(
                [orchestrator],
                NullLogger<SemanticAnalysisPipeline>.Instance);
            await pipeline.AnalyzeAsync(context);

            Assert.Equal(2, context.SemanticAnalysisRuns.Count);
            Assert.All(context.SemanticAnalysisRuns, run =>
                Assert.Equal(SemanticAnalysisStatus.PartiallyCompleted, run.Status));
            Assert.Equal(2, context.ProductionComponents.Count);
            var edge = Assert.Single(context.ProductionComponentDependencies);
            Assert.Equal(1, edge.RelationshipCount);
            Assert.Contains(applicationFile, edge.EvidenceFiles);
            Assert.Equal(
                "sample.application",
                context.ProductionComponents.Single(component => component.Id == edge.SourceComponentId).QualifiedName);
            Assert.Equal(
                "sample.domain",
                context.ProductionComponents.Single(component => component.Id == edge.TargetComponentId).QualifiedName);
            Assert.Equal("sample.application", edge.SourceComponentName);
            Assert.Equal("sample.domain", edge.TargetComponentName);
            Assert.DoesNotContain("Template", edge.SourceComponentName, StringComparison.Ordinal);
            Assert.DoesNotContain("Template", edge.TargetComponentName, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(projectPath, recursive: true);
        }
    }


    [Fact]
    public void Projects_python_package_components_and_relationships_without_build_module_boundaries()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), $"aegis-python-graph-{Guid.NewGuid():N}");
        var applicationFile = Path.Combine(projectPath, "application", "services.py");
        var domainFile = Path.Combine(projectPath, "domain", "order.py");
        var context = new ProjectArchitectureContext
        {
            ProjectName = "sample-python",
            RootPath = projectPath,
            Language = "Python",
            Modules =
            [
                new ProjectModuleContext
                {
                    Name = "sample-python",
                    Path = projectPath,
                    Languages = ["Python"]
                }
            ],
            SemanticAnalysisRuns =
            [
                new SemanticAnalysisRun
                {
                    RunId = "python-run",
                    Language = "Python",
                    EngineId = "python-test-provider",
                    ProjectPath = projectPath,
                    Status = SemanticAnalysisStatus.PartiallyCompleted,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "python-service",
                            Name = "OrderService",
                            QualifiedName = "application.services.OrderService",
                            ContainerName = "application",
                            ContainerKind = "Package",
                            Language = "Python",
                            ProjectPath = projectPath,
                            FilePath = applicationFile,
                            Origin = SemanticSymbolOrigin.Source
                        },
                        new SemanticSymbol
                        {
                            Id = "python-order",
                            Name = "Order",
                            QualifiedName = "domain.order.Order",
                            ContainerName = "domain",
                            ContainerKind = "Package",
                            Language = "Python",
                            ProjectPath = projectPath,
                            FilePath = domainFile,
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ],
                    Relationships =
                    [
                        new SemanticRelationship
                        {
                            Id = "python-service-uses-domain",
                            SourceSymbolId = "python-service",
                            TargetSymbolId = "python-order",
                            Kind = SemanticRelationshipKind.References,
                            ResolutionStatus = SemanticResolutionStatus.PartiallyResolved,
                            Provenance =
                            [
                                new SemanticEvidenceProvenance
                                {
                                    AnalyzerId = "python-test-provider",
                                    Source = SemanticEvidenceSource.Compiler,
                                    FilePath = applicationFile,
                                    Confidence = 1.0
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        SemanticArchitectureComponentDetector.Analyze(context);

        Assert.Equal(2, context.ProductionComponents.Count);
        Assert.All(context.ProductionComponents, component =>
        {
            Assert.Equal("Python", component.Language);
            Assert.Equal("Package", component.ComponentKind);
        });

        var edge = Assert.Single(context.ProductionComponentDependencies);
        Assert.Equal(1, edge.RelationshipCount);
        Assert.Equal(0, edge.ResolvedRelationshipCount);
        Assert.Equal(1, edge.PartiallyResolvedRelationshipCount);
        Assert.Equal("application", context.ProductionComponents
            .Single(component => component.Id == edge.SourceComponentId).QualifiedName);
        Assert.Equal("domain", context.ProductionComponents
            .Single(component => component.Id == edge.TargetComponentId).QualifiedName);
        Assert.Contains(applicationFile, edge.EvidenceFiles);
    }


    [Fact]
    public void Uses_csharp_build_projects_as_architectural_components_instead_of_namespaces()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aegis-csharp-graph-{Guid.NewGuid():N}");
        var apiPath = Path.Combine(root, "src", "Api");
        var domainPath = Path.Combine(root, "src", "Domain");
        var apiFile = Path.Combine(apiPath, "OrderService.cs");
        var domainFile = Path.Combine(domainPath, "Order.cs");
        var context = new ProjectArchitectureContext
        {
            ProjectName = "Sample",
            RootPath = root,
            Language = "C#",
            Modules =
            [
                new ProjectModuleContext { Name = "Api", Path = apiPath, Languages = ["C#"] },
                new ProjectModuleContext { Name = "Domain", Path = domainPath, Languages = ["C#"] }
            ],
            SemanticAnalysisRuns =
            [
                new SemanticAnalysisRun
                {
                    RunId = "api-run",
                    Language = "C#",
                    EngineId = "roslyn-test-provider",
                    ProjectPath = apiPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "api-service",
                            Name = "OrderService",
                            QualifiedName = "Sample.Api.OrderService",
                            ContainerName = "Sample.Api",
                            ContainerKind = "Namespace",
                            Language = "C#",
                            ProjectPath = apiPath,
                            FilePath = apiFile,
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ],
                    Relationships =
                    [
                        new SemanticRelationship
                        {
                            Id = "api-uses-domain",
                            SourceSymbolId = "api-service",
                            TargetSymbolId = "domain-order",
                            Kind = SemanticRelationshipKind.References,
                            ResolutionStatus = SemanticResolutionStatus.Resolved,
                            Provenance =
                            [
                                new SemanticEvidenceProvenance
                                {
                                    AnalyzerId = "roslyn-test-provider",
                                    Source = SemanticEvidenceSource.Compiler,
                                    FilePath = apiFile,
                                    Confidence = 1.0
                                }
                            ]
                        }
                    ]
                },
                new SemanticAnalysisRun
                {
                    RunId = "domain-run",
                    Language = "C#",
                    EngineId = "roslyn-test-provider",
                    ProjectPath = domainPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "domain-order",
                            Name = "Order",
                            QualifiedName = "Sample.Domain.Order",
                            ContainerName = "Sample.Domain",
                            ContainerKind = "Namespace",
                            Language = "C#",
                            ProjectPath = domainPath,
                            FilePath = domainFile,
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ]
                }
            ]
        };

        SemanticArchitectureComponentDetector.Analyze(context);

        Assert.Equal(2, context.ProductionComponents.Count);
        Assert.All(context.ProductionComponents, component =>
            Assert.Equal("Project", component.ComponentKind));
        Assert.Contains(context.ProductionComponents, component =>
            component.BuildModuleName == "Api" && component.QualifiedName == "Api");
        Assert.Contains(context.ProductionComponents, component =>
            component.BuildModuleName == "Domain" && component.QualifiedName == "Domain");

        var edge = Assert.Single(context.ProductionComponentDependencies);
        Assert.Equal(1, edge.RelationshipCount);
        Assert.Equal("Api", context.ProductionComponents
            .Single(component => component.Id == edge.SourceComponentId).BuildModuleName);
        Assert.Equal("Domain", context.ProductionComponents
            .Single(component => component.Id == edge.TargetComponentId).BuildModuleName);
    }

    private sealed class PartialPackageGraphAdapter(string applicationFile, string domainFile)
        : ISemanticAnalysisAdapter
    {
        public string AdapterId => "eclipse-jdt";

        public string Language => "Java";

        public IReadOnlyCollection<string> SupportedCapabilities => ["symbols", "relationships"];

        public bool CanAnalyze(SemanticAnalysisRequest request) => true;

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            var symbols = new[]
            {
                new SemanticSymbol
                {
                    Id = "application-type",
                    Name = "OrderService",
                    QualifiedName = "sample.application.OrderService",
                    ContainerName = "sample.application",
                    ContainerKind = "Package",
                    Language = "Java",
                    ProjectPath = request.ProjectPath,
                    FilePath = applicationFile,
                    Origin = SemanticSymbolOrigin.Source
                },
                new SemanticSymbol
                {
                    Id = "domain-type",
                    Name = "Order",
                    QualifiedName = "sample.domain.Order",
                    ContainerName = "sample.domain",
                    ContainerKind = "Package",
                    Language = "Java",
                    ProjectPath = request.ProjectPath,
                    FilePath = domainFile,
                    Origin = SemanticSymbolOrigin.Source
                }
            };

            return ValueTask.FromResult(new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = "Java",
                EngineId = AdapterId,
                EngineVersion = "3.47.0",
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.PartiallyCompleted,
                AnalyzedFileCount = 2,
                Symbols = [.. symbols],
                Relationships =
                [
                    new SemanticRelationship
                    {
                        Id = "application-uses-domain",
                        SourceSymbolId = "application-type",
                        TargetSymbolId = "domain-type",
                        Kind = SemanticRelationshipKind.References,
                        ResolutionStatus = SemanticResolutionStatus.Resolved,
                        Provenance =
                        [
                            new SemanticEvidenceProvenance
                            {
                                AnalyzerId = AdapterId,
                                Source = SemanticEvidenceSource.Compiler,
                                FilePath = applicationFile,
                                Confidence = 1.0
                            }
                        ]
                    }
                ]
            });
        }
    }

    private sealed class EmptyClasspathResolver : IJavaClasspathResolver
    {
        public JavaClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode) =>
            new([], [], false, true);
    }
}
