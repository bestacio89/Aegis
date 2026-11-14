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
            var relationshipReference = Assert.Single(edge.ContributingRelationships);
            Assert.Contains(context.SemanticAnalysisRuns, run =>
                run.RunId == relationshipReference.RunId
                && run.Relationships.Any(relationship =>
                    relationship.Id == relationshipReference.RelationshipId));
            Assert.Equal(0, edge.UnreferencedRelationshipCount);
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
        Assert.Equal("python-run", Assert.Single(edge.ContributingRelationships).RunId);
        Assert.Equal("python-service-uses-domain", edge.ContributingRelationships[0].RelationshipId);
        Assert.Equal(0, edge.UnreferencedRelationshipCount);
        Assert.Equal(0, edge.ResolvedRelationshipCount);
        Assert.Equal(1, edge.PartiallyResolvedRelationshipCount);
        Assert.Equal("application", context.ProductionComponents
            .Single(component => component.Id == edge.SourceComponentId).QualifiedName);
        Assert.Equal("domain", context.ProductionComponents
            .Single(component => component.Id == edge.TargetComponentId).QualifiedName);
        Assert.Contains(applicationFile, edge.EvidenceFiles);
    }


    [Fact]
    public void Resolves_cross_module_relationships_using_external_symbol_project_identity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"semantic-cross-module-{Guid.NewGuid():N}");
        var appPath = Path.Combine(root, "app");
        var firstLibraryPath = Path.Combine(root, "first-library");
        var secondLibraryPath = Path.Combine(root, "second-library");
        var context = new ProjectArchitectureContext
        {
            ProjectName = "multi-module",
            RootPath = root,
            Language = "Kotlin",
            Modules =
            [
                new ProjectModuleContext { Name = "app", Path = appPath, Languages = ["Kotlin"] },
                new ProjectModuleContext { Name = "first-library", Path = firstLibraryPath, Languages = ["Kotlin"] },
                new ProjectModuleContext { Name = "second-library", Path = secondLibraryPath, Languages = ["Kotlin"] }
            ],
            SemanticAnalysisRuns =
            [
                new SemanticAnalysisRun
                {
                    RunId = "app-run",
                    Language = "Kotlin",
                    EngineId = "kotlin-k2-test-provider",
                    ProjectPath = appPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "app-service",
                            Name = "AppService",
                            QualifiedName = "sample.app.AppService",
                            ContainerName = "sample.app",
                            ContainerKind = "Package",
                            Language = "Kotlin",
                            ProjectPath = appPath,
                            FilePath = Path.Combine(appPath, "AppService.kt"),
                            Origin = SemanticSymbolOrigin.Source
                        },
                        new SemanticSymbol
                        {
                            Id = "shared-library-symbol",
                            Name = "LibraryService",
                            QualifiedName = "sample.second.LibraryService",
                            ContainerName = "sample.second",
                            ContainerKind = "Package",
                            Language = "Kotlin",
                            ProjectPath = secondLibraryPath,
                            Origin = SemanticSymbolOrigin.External
                        }
                    ],
                    Relationships =
                    [
                        new SemanticRelationship
                        {
                            Id = "app-uses-library",
                            SourceSymbolId = "app-service",
                            TargetSymbolId = "shared-library-symbol",
                            Kind = SemanticRelationshipKind.Calls,
                            ResolutionStatus = SemanticResolutionStatus.Resolved
                        }
                    ]
                },
                new SemanticAnalysisRun
                {
                    RunId = "first-library-run",
                    Language = "Kotlin",
                    EngineId = "kotlin-k2-test-provider",
                    ProjectPath = firstLibraryPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "shared-library-symbol",
                            Name = "LibraryService",
                            QualifiedName = "sample.first.LibraryService",
                            ContainerName = "sample.first",
                            ContainerKind = "Package",
                            Language = "Kotlin",
                            ProjectPath = firstLibraryPath,
                            FilePath = Path.Combine(firstLibraryPath, "LibraryService.kt"),
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ]
                },
                new SemanticAnalysisRun
                {
                    RunId = "second-library-run",
                    Language = "Kotlin",
                    EngineId = "kotlin-k2-test-provider",
                    ProjectPath = secondLibraryPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "shared-library-symbol",
                            Name = "LibraryService",
                            QualifiedName = "sample.second.LibraryService",
                            ContainerName = "sample.second",
                            ContainerKind = "Package",
                            Language = "Kotlin",
                            ProjectPath = secondLibraryPath,
                            FilePath = Path.Combine(secondLibraryPath, "LibraryService.kt"),
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ]
                }
            ]
        };

        SemanticArchitectureComponentDetector.Analyze(context);

        var edge = Assert.Single(context.ProductionComponentDependencies);
        Assert.Equal("app", context.ProductionComponents
            .Single(component => component.Id == edge.SourceComponentId).BuildModuleName);
        Assert.Equal("second-library", context.ProductionComponents
            .Single(component => component.Id == edge.TargetComponentId).BuildModuleName);
        Assert.Empty(context.TestComponentDependencies);
        Assert.Empty(context.Dependencies);
    }

    [Fact]
    public void Does_not_guess_cross_module_target_when_project_scoped_symbol_id_is_ambiguous()
    {
        var root = Path.Combine(Path.GetTempPath(), $"semantic-ambiguous-target-{Guid.NewGuid():N}");
        var appPath = Path.Combine(root, "app");
        var firstLibraryPath = Path.Combine(root, "first-library");
        var secondLibraryPath = Path.Combine(root, "second-library");
        var context = new ProjectArchitectureContext
        {
            ProjectName = "multi-module",
            RootPath = root,
            Language = "Java",
            Modules =
            [
                new ProjectModuleContext { Name = "app", Path = appPath, Languages = ["Java"] },
                new ProjectModuleContext { Name = "first-library", Path = firstLibraryPath, Languages = ["Java"] },
                new ProjectModuleContext { Name = "second-library", Path = secondLibraryPath, Languages = ["Java"] }
            ],
            SemanticAnalysisRuns =
            [
                new SemanticAnalysisRun
                {
                    RunId = "app-run",
                    Language = "Java",
                    EngineId = "jdt-test-provider",
                    ProjectPath = appPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "app-service",
                            Name = "AppService",
                            QualifiedName = "sample.app.AppService",
                            ContainerName = "sample.app",
                            ContainerKind = "Package",
                            Language = "Java",
                            ProjectPath = appPath,
                            FilePath = Path.Combine(appPath, "AppService.java"),
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ],
                    Relationships =
                    [
                        new SemanticRelationship
                        {
                            Id = "app-uses-ambiguous-library",
                            SourceSymbolId = "app-service",
                            TargetSymbolId = "shared-library-symbol",
                            Kind = SemanticRelationshipKind.References,
                            ResolutionStatus = SemanticResolutionStatus.Resolved
                        }
                    ]
                },
                new SemanticAnalysisRun
                {
                    RunId = "first-library-run",
                    Language = "Java",
                    EngineId = "jdt-test-provider",
                    ProjectPath = firstLibraryPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "shared-library-symbol",
                            Name = "LibraryService",
                            QualifiedName = "sample.first.LibraryService",
                            ContainerName = "sample.first",
                            ContainerKind = "Package",
                            Language = "Java",
                            ProjectPath = firstLibraryPath,
                            FilePath = Path.Combine(firstLibraryPath, "LibraryService.java"),
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ]
                },
                new SemanticAnalysisRun
                {
                    RunId = "second-library-run",
                    Language = "Java",
                    EngineId = "jdt-test-provider",
                    ProjectPath = secondLibraryPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "shared-library-symbol",
                            Name = "LibraryService",
                            QualifiedName = "sample.second.LibraryService",
                            ContainerName = "sample.second",
                            ContainerKind = "Package",
                            Language = "Java",
                            ProjectPath = secondLibraryPath,
                            FilePath = Path.Combine(secondLibraryPath, "LibraryService.java"),
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ]
                }
            ]
        };

        SemanticArchitectureComponentDetector.Analyze(context);

        Assert.Equal(3, context.ProductionComponents.Count);
        Assert.Empty(context.ProductionComponentDependencies);
        Assert.Empty(context.TestComponentDependencies);
    }

    [Fact]
    public void Projects_android_variants_and_keeps_instrumentation_relationships_out_of_production_graph()
    {
        var modulePath = Path.Combine(Path.GetTempPath(), $"aegis-android-projection-{Guid.NewGuid():N}");
        var mainRoot = Path.Combine(modulePath, "src", "main", "java");
        var androidTestRoot = Path.Combine(modulePath, "src", "androidTest", "java");
        var mainFile = Path.Combine(mainRoot, "sample", "application", "MainActivity.kt");
        var testFile = Path.Combine(androidTestRoot, "sample", "tests", "MainActivityTest.kt");
        var module = new ProjectModuleContext
        {
            Name = "app",
            Path = modulePath,
            Languages = ["Kotlin"],
            GradleBuildModel = new GradleBuildModel
            {
                Android = new GradleAndroidBuildModel
                {
                    PluginId = "com.android.application",
                    ModuleKind = "Application",
                    Variants = [new GradleAndroidVariant("debug", [], "debug"), new GradleAndroidVariant("release", [], "release")],
                    SourceSets =
                    [
                        new GradleAndroidSourceSet
                        {
                            Name = "main",
                            Ownership = ProjectSourceRootOwnership.Production,
                            JavaSourceDirectories = [mainRoot],
                            AppliesToVariants = ["debug", "release"]
                        },
                        new GradleAndroidSourceSet
                        {
                            Name = "androidTest",
                            Ownership = ProjectSourceRootOwnership.Test,
                            TestKind = GradleAndroidTestKind.Instrumentation,
                            JavaSourceDirectories = [androidTestRoot],
                            AppliesToVariants = ["debug", "release"]
                        }
                    ]
                }
            }
        };
        var context = new ProjectArchitectureContext
        {
            ProjectName = "android-sample",
            RootPath = modulePath,
            Language = "Kotlin",
            Modules = [module],
            SemanticAnalysisRuns =
            [
                new SemanticAnalysisRun
                {
                    RunId = "android-run",
                    Language = "Kotlin",
                    EngineId = "kotlin-k2-ir",
                    ProjectPath = modulePath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "android-main",
                            Name = "MainActivity",
                            QualifiedName = "sample.application.MainActivity",
                            ContainerName = "sample.application",
                            ContainerKind = "Package",
                            Language = "Kotlin",
                            ProjectPath = modulePath,
                            FilePath = mainFile,
                            Origin = SemanticSymbolOrigin.Source
                        },
                        new SemanticSymbol
                        {
                            Id = "android-test",
                            Name = "MainActivityTest",
                            QualifiedName = "sample.tests.MainActivityTest",
                            ContainerName = "sample.tests",
                            ContainerKind = "Package",
                            Language = "Kotlin",
                            ProjectPath = modulePath,
                            FilePath = testFile,
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ],
                    Relationships =
                    [
                        new SemanticRelationship
                        {
                            Id = "instrumentation-uses-app",
                            SourceSymbolId = "android-test",
                            TargetSymbolId = "android-main",
                            Kind = SemanticRelationshipKind.Calls,
                            ResolutionStatus = SemanticResolutionStatus.Resolved
                        }
                    ]
                }
            ]
        };

        SemanticArchitectureComponentDetector.Analyze(context);

        var production = Assert.Single(context.ProductionComponents);
        Assert.Equal(["debug", "release"], production.AndroidVariants);
        Assert.Equal(["main"], production.AndroidSourceSets);
        Assert.Equal("Application", production.AndroidModuleKind);
        Assert.Empty(context.ProductionComponentDependencies);
        var testEdge = Assert.Single(context.TestComponentDependencies);
        Assert.Equal("Test", testEdge.Scope);
        Assert.Equal(["debug", "release"], testEdge.AndroidVariants);
        Assert.Equal(["androidTest", "main"], testEdge.AndroidSourceSets);
        Assert.Equal(["Instrumentation"], testEdge.AndroidTestKinds);
        Assert.Empty(context.ProductionComponentDependencies);
    }

    [Fact]
    public void Counts_relationships_without_provider_ids_and_exposes_the_traceability_gap()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), $"aegis-unreferenced-graph-{Guid.NewGuid():N}");
        var context = new ProjectArchitectureContext
        {
            ProjectName = "sample-java",
            RootPath = projectPath,
            Language = "Java",
            SemanticAnalysisRuns =
            [
                new SemanticAnalysisRun
                {
                    RunId = "java-run",
                    Language = "Java",
                    EngineId = "jdt-test-provider",
                    ProjectPath = projectPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols =
                    [
                        new SemanticSymbol
                        {
                            Id = "source-type",
                            Name = "OrderService",
                            QualifiedName = "sample.application.OrderService",
                            ContainerName = "sample.application",
                            ContainerKind = "Package",
                            Language = "Java",
                            ProjectPath = projectPath,
                            FilePath = Path.Combine(projectPath, "OrderService.java"),
                            Origin = SemanticSymbolOrigin.Source
                        },
                        new SemanticSymbol
                        {
                            Id = "target-type",
                            Name = "Order",
                            QualifiedName = "sample.domain.Order",
                            ContainerName = "sample.domain",
                            ContainerKind = "Package",
                            Language = "Java",
                            ProjectPath = projectPath,
                            FilePath = Path.Combine(projectPath, "Order.java"),
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ],
                    Relationships =
                    [
                        new SemanticRelationship
                        {
                            SourceSymbolId = "source-type",
                            TargetSymbolId = "target-type",
                            Kind = SemanticRelationshipKind.References,
                            ResolutionStatus = SemanticResolutionStatus.Resolved
                        }
                    ]
                }
            ]
        };

        SemanticArchitectureComponentDetector.Analyze(context);

        var edge = Assert.Single(context.ProductionComponentDependencies);
        Assert.Equal(1, edge.RelationshipCount);
        Assert.Empty(edge.ContributingRelationships);
        Assert.Equal(1, edge.UnreferencedRelationshipCount);
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

    private sealed class EmptyClasspathResolver : IJvmClasspathResolver
    {
        public JvmClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode) =>
            new([], [], false, true);
    }
}
