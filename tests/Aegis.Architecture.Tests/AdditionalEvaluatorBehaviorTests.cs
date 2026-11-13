using Aegis.Architecture.Bootstrap;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class AdditionalEvaluatorBehaviorTests
{
    [Fact]
    public async Task ApiConsistencyEvaluator_detects_inconsistent_routes_and_reports_a_summary()
    {
        using var project = new TemporaryProject();
        project.Write("back/OrdersController.cs", """
            public sealed class OrdersController
            {
                [HttpGet("/API/Orders/")]
                public void GetOrders() { }

                [HttpPost("/orders/orders")]
                public void CreateOrder() { }
            }
            """);

        var results = await Evaluate(project, "ApiConsistencyEvaluator", "C#", "ASP.NET");

        var fileResult = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("FileName") == "OrdersController.cs"
                && result.Metrics.ContainsKey("ApiConsistencyIndex"));

        Assert.True(fileResult.Metrics["UppercaseRouteViolations"] >= 1);
        Assert.True(fileResult.Metrics["TrailingSlashViolations"] >= 1);
        Assert.True(fileResult.Metrics["MissingVersionViolations"] >= 1);
        Assert.Equal(100, fileResult.Metrics["EndpointDiscoveryScore"]);
        var summary = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "Project"
                && result.Metrics.ContainsKey("ApiMaturityIndex"));

        // AEG-ARCH-R7 is project-scoped and declares ApiConsistencyIndex.
        // The summary must emit that exact contract, not only a differently
        // named alias.
        Assert.True(summary.Metrics.ContainsKey("ApiConsistencyIndex"));
        Assert.Equal(
            summary.Metrics["ApiConsistencyIndex"],
            summary.Metrics["AverageApiConsistencyIndex"]);
        Assert.Equal("true", summary.Metadata["AnalysisComplete"]);
        AssertAllMetricsAreFinite(results);
    }


    [Fact]
    public async Task CircularDependencyEvaluator_detects_cycles_in_language_native_component_graph()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "Java", "Spring Boot");
        context.ArchitectureComponents.AddRange(
        [
            new ArchitectureComponentContext
            {
                Id = "java-package:application",
                Name = "application",
                QualifiedName = "sample.application",
                Language = "Java",
                ComponentKind = "Package",
                BuildModuleName = "Template",
                ProductionSymbolCount = 2
            },
            new ArchitectureComponentContext
            {
                Id = "java-package:domain",
                Name = "domain",
                QualifiedName = "sample.domain",
                Language = "Java",
                ComponentKind = "Package",
                BuildModuleName = "Template",
                ProductionSymbolCount = 2
            }
        ]);
        context.ArchitectureComponentDependencies.AddRange(
        [
            new ArchitectureComponentDependencyContext
            {
                SourceComponentId = "java-package:application",
                SourceComponentName = "Template · sample.application",
                TargetComponentId = "java-package:domain",
                TargetComponentName = "Template · sample.domain",
                RelationshipCount = 1
            },
            new ArchitectureComponentDependencyContext
            {
                SourceComponentId = "java-package:domain",
                SourceComponentName = "Template · sample.domain",
                TargetComponentId = "java-package:application",
                TargetComponentName = "Template · sample.application",
                RelationshipCount = 1
            }
        ]);
        context.SemanticAnalysisRuns.Add(new SemanticAnalysisRun
        {
            Language = "Java",
            EngineId = "eclipse-jdt",
            ProjectPath = project.Root,
            Status = SemanticAnalysisStatus.Completed
        });

        var results = await Evaluate(project, "CircularDependencyEvaluator", context);

        Assert.Contains(results, result =>
            result.Metrics.GetValueOrDefault("CircularDependencyViolation") == 1
            && result.Metadata.GetValueOrDefault("GraphLevel") == "ArchitecturalComponents");
        var summary = Assert.Single(results, result =>
            result.Metrics.ContainsKey("CircularDependencyCount"));
        Assert.Equal(1, summary.Metrics["CircularDependencyCount"]);
        Assert.Contains("sample.application", results
            .Single(result => result.Metrics.GetValueOrDefault("CircularDependencyViolation") == 1)
            .Metadata["Cycle"]);
        Assert.Equal("true", summary.Metadata["AnalysisCompleted"]);
    }

    [Fact]
    public async Task CircularDependencyEvaluator_detects_a_cycle_in_the_supplied_dependency_graph()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "C#", "ASP.NET Core");
        context.Dependencies.AddRange(
        [
            new ArchitectureDependencyContext
            {
                Source = "Orders",
                Target = "Payments",
                SourceLayer = "Application",
                TargetLayer = "Infrastructure",
                DependencyType = "Project"
            },
            new ArchitectureDependencyContext
            {
                Source = "Payments",
                Target = "Orders",
                SourceLayer = "Infrastructure",
                TargetLayer = "Application",
                DependencyType = "Project"
            },
            new ArchitectureDependencyContext
            {
                Source = "TestA",
                Target = "TestB",
                SourceLayer = "Tests",
                TargetLayer = "Tests",
                DependencyType = "Project",
                Scope = "Test"
            },
            new ArchitectureDependencyContext
            {
                Source = "TestB",
                Target = "TestA",
                SourceLayer = "Tests",
                TargetLayer = "Tests",
                DependencyType = "Project",
                Scope = "Test"
            }
        ]);

        var results = await Evaluate(project, "CircularDependencyEvaluator", context);

        Assert.Contains(results, result =>
            result.Metrics.GetValueOrDefault("HasCycle") == 1
            && result.Metrics.GetValueOrDefault("CircularDependencyViolation") == 1);
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("CircularDependencyCount"));
        Assert.Equal(1, summary.Metrics["CircularDependencyCount"]);
    }

    [Fact]
    public async Task CircularDependencyEvaluator_reports_completed_clean_analysis_for_an_acyclic_graph()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "C#", "ASP.NET Core");
        context.Dependencies.AddRange(
        [
            new ArchitectureDependencyContext
            {
                Source = "Api",
                Target = "Application",
                SourceLayer = "Presentation",
                TargetLayer = "Application"
            },
            new ArchitectureDependencyContext
            {
                Source = "Application",
                Target = "Domain",
                SourceLayer = "Application",
                TargetLayer = "Domain"
            }
        ]);

        var results = await Evaluate(project, "CircularDependencyEvaluator", context);
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("CircularDependencyCount"));

        Assert.Equal(0, summary.Metrics["CircularDependencyCount"]);
        Assert.Equal("true", summary.Metadata["AnalysisCompleted"]);
        Assert.DoesNotContain(results, result => result.Metrics.GetValueOrDefault("HasCycle") == 1);
    }

    [Fact]
    public async Task DependencyManifest_evaluators_count_declarations_without_claiming_usage_or_registry_validation()
    {
        using var project = new TemporaryProject();
        project.Write("back/package.json", """
            {
              "name": "fixture",
              "dependencies": {
                "express": "^4.18.0",
                "left-pad": "latest"
              },
              "devDependencies": {
                "typescript": "~5.0.0"
              }
            }
            """);

        var versionResults = await Evaluate(project, "PackageVersionEvaluator", "JavaScript", "Node");
        var versionSummary = Assert.Single(
            versionResults,
            result => result.Metrics.ContainsKey("VersionDeclarations"));

        Assert.True(versionSummary.Metrics["VersionDeclarations"] >= 3);
        Assert.Equal(0, versionSummary.Metrics["ParseFailureCount"]);
        Assert.Equal(bool.FalseString, versionSummary.Metadata["OutdatedPackagesChecked"]);
        Assert.Equal(bool.FalseString, versionSummary.Metadata["RegistryComparisonPerformed"]);

        var referenceResults = await Evaluate(project, "UnusedReferenceEvaluator", "JavaScript", "Node");
        var referenceSummary = Assert.Single(
            referenceResults,
            result => result.Metrics.ContainsKey("DeclaredReferences")
                && result.Metadata.GetValueOrDefault("EvaluationScope") == "Project");

        Assert.True(referenceSummary.Metrics["DeclaredReferences"] >= 3);
        Assert.Equal(bool.FalseString, referenceSummary.Metadata["UnusedReferencesDetermined"]);
        AssertAllMetricsAreFinite(versionResults);
        AssertAllMetricsAreFinite(referenceResults);
    }

    [Fact]
    public async Task CohesionEvaluator_reports_structural_measurements_for_a_type()
    {
        using var project = new TemporaryProject();
        var methods = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 15).Select(index =>
                $"    public void Operation{index}() {{ }}"));

        project.Write(
            "back/OrderProcessor.cs",
            $@"public sealed class OrderProcessor
{{
    private int _count;
    private string _name = string.Empty;
{methods}
}}");

        var results = await Evaluate(project, "CohesionEvaluator", "C#", "ASP.NET");
        var result = Assert.Single(
            results,
            item => item.Metrics.ContainsKey("FieldCount")
                && item.Metrics.ContainsKey("MethodCount"));

        Assert.True(result.Metrics["FieldCount"] >= 2);
        Assert.True(result.Metrics["MethodCount"] >= 2);
        Assert.True(result.Metrics.ContainsKey("MethodFieldRatio"));
        AssertAllMetricsAreFinite(results);
    }


    [Fact]
    public async Task ErrorHandlingEvaluator_emits_coverage_only_when_catch_evidence_exists()
    {
        using var project = new TemporaryProject();
        project.Write("back/OrderService.cs", """
            public sealed class OrderService
            {
                public void Execute()
                {
                    try
                    {
                        Run();
                    }
                    catch (System.Exception)
                    {
                    }
                }

                private void Run() { }
            }
            """);

        var results = await Evaluate(project, "ErrorHandlingEvaluator", "C#", "ASP.NET Core");
        var summary = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "Project"
                && result.Metrics.ContainsKey("ErrorHandlingCoverage"));

        Assert.Equal(1, summary.Metrics["TotalCatchBlocks"]);
        Assert.Equal(1, summary.Metrics["SwallowedExceptions"]);
        Assert.Equal(0, summary.Metrics["ErrorHandlingCoverage"]);
        Assert.Equal(bool.TrueString, summary.Metadata["AnalysisComplete"]);
    }

    [Fact]
    public async Task MaintainabilityEvaluator_reports_file_and_project_measurements()
    {
        using var project = new TemporaryProject();
        project.Write("back/MaintainabilityFixture.cs", """
            // A small, intentionally documented fixture.
            public sealed class MaintainabilityFixture
            {
                // Keep the method simple.
                public bool IsValid(bool value)
                {
                    if (value)
                    {
                        return true;
                    }

                    return false;
                }
            }
            """);

        var results = await Evaluate(project, "MaintainabilityEvaluator", "C#", "ASP.NET Core");

        var fileResult = Assert.Single(results, result =>
            result.Metrics.ContainsKey("DecisionPointCount")
            && result.Metrics.ContainsKey("CommentDensity")
            && result.Metadata.GetValueOrDefault("EvaluationScope") == "File");
        Assert.True(fileResult.Metrics.ContainsKey("MaintainabilityScore"));
        Assert.Contains("Aegis heuristic", fileResult.Metadata["MaintainabilityMeasurement"]);
        Assert.Contains("informational only", fileResult.Metadata["CommentDensityInterpretation"]);
        Assert.Equal("FileMetrics", fileResult.Metadata["EvidenceScope.MaintainabilityScore"]);
        Assert.Contains("not included in the score", fileResult.Metadata["EvidenceSummary.MaintainabilityScore"]);
        Assert.InRange(fileResult.Metrics["MaintainabilityScore"], 0, 100);

        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("AnalyzedFiles"));

        Assert.True(summary.Metrics["AnalyzedFiles"] >= 1);
        Assert.True(summary.Metrics["TotalNonEmptyLines"] > 0);
        Assert.True(summary.Metrics["AverageMaintainabilityScore"] >= 0);
        Assert.Equal("true", summary.Metadata["AnalysisComplete"]);
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task MaintainabilityEvaluator_reports_an_empty_scope_without_marking_analysis_incomplete()
    {
        using var project = new TemporaryProject();

        var results = await Evaluate(
            project,
            "MaintainabilityEvaluator",
            "C#",
            "ASP.NET Core");

        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("FilesDiscovered"));

        Assert.Equal(0, summary.Metrics["FilesDiscovered"]);
        Assert.Equal(0, summary.Metrics["AnalyzedFiles"]);
        Assert.Equal("true", summary.Metadata["AnalysisComplete"]);
        Assert.Equal("NoSourceFiles", summary.Metadata["AnalysisStatus"]);
        Assert.DoesNotContain("IncompleteReason", summary.Metadata.Keys);
    }

    [Fact]
    public async Task ArchitecturalEvaluator_skips_mixed_library_scopes_without_layer_boundaries()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "C#", ".NET");
        context.ArchitectureStyle = "Multi-Project / Mixed";
        context.Modules =
        [
            new() { Name = "Franz.Common", Path = project.Root },
            new() { Name = "Franz.Common.Client", Path = Path.Combine(project.Root, "Client") }
        ];
        context.Layers =
        [
            new() { Name = "Client", Path = project.Root },
            new() { Name = "Tests", Path = Path.Combine(project.Root, "Tests") }
        ];
        context.Dependencies.Add(
            new ArchitectureDependencyContext
            {
                File = Path.Combine(project.Root, "back", "Unclassified.cs"),
                Source = "UnclassifiedA",
                Target = "UnclassifiedB"
            });

        var results = await Evaluate(
            project,
            "ArchitecturalEvaluator",
            context);

        Assert.Empty(results);
    }

    [Fact]
    public async Task DependencyGraphEvaluator_reports_observed_component_edges_without_scoring_incomplete_analysis()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "Java", "Spring Boot");
        context.ArchitectureComponents.AddRange(
        [
            new ArchitectureComponentContext
            {
                Id = "package:application",
                Name = "application",
                QualifiedName = "sample.application",
                Language = "Java",
                ComponentKind = "Package",
                ProductionSymbolCount = 4
            },
            new ArchitectureComponentContext
            {
                Id = "package:domain",
                Name = "domain",
                QualifiedName = "sample.domain",
                Language = "Java",
                ComponentKind = "Package",
                ProductionSymbolCount = 3
            },
            new ArchitectureComponentContext
            {
                Id = "package:utilities",
                Name = "utilities",
                QualifiedName = "sample.utilities",
                Language = "Java",
                ComponentKind = "Package",
                ProductionSymbolCount = 1
            }
        ]);
        context.ArchitectureComponentDependencies.Add(
            new ArchitectureComponentDependencyContext
            {
                SourceComponentId = "package:application",
                SourceComponentName = "sample.application",
                TargetComponentId = "package:domain",
                TargetComponentName = "sample.domain",
                RelationshipCount = 2
            });
        context.Dependencies.Add(new ArchitectureDependencyContext
        {
            Source = "Template",
            Target = "TemplateChild",
            SourceLayer = "Application",
            TargetLayer = "Application",
            DependencyType = "BuildModule",
            Scope = "Production"
        });
        context.SemanticAnalysisRuns.Add(
            new SemanticAnalysisRun
            {
                Language = "Java",
                EngineId = "eclipse-jdt",
                Status = SemanticAnalysisStatus.PartiallyCompleted
            });

        var results = await Evaluate(project, "DependencyGraphEvaluator", context);
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("TotalRelations"));

        Assert.Equal(2, summary.Metrics["TotalRelations"]);
        Assert.False(summary.Metrics.ContainsKey("DependencyGraphDensity"));
        Assert.Equal("ArchitecturalComponents", summary.Metadata["GraphLevel"]);
        Assert.Equal("3", summary.Metadata["GraphNodeCount"]);
        Assert.Equal("1", summary.Metadata["GraphEdgeCount"]);
        Assert.Equal("false", summary.Metadata["AnalysisComplete"]);
        Assert.Equal("Unavailable: semantic analysis is incomplete.", summary.Metadata["GraphDensityStatus"]);
    }

    [Fact]
    public async Task DependencyGraphEvaluator_measures_semantic_component_density_when_analysis_is_complete()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "Java", "Spring Boot");
        context.ArchitectureComponents.AddRange(
        [
            new ArchitectureComponentContext
            {
                Id = "package:application",
                Name = "application",
                QualifiedName = "sample.application",
                Language = "Java",
                ComponentKind = "Package",
                ProductionSymbolCount = 4
            },
            new ArchitectureComponentContext
            {
                Id = "package:domain",
                Name = "domain",
                QualifiedName = "sample.domain",
                Language = "Java",
                ComponentKind = "Package",
                ProductionSymbolCount = 3
            },
            new ArchitectureComponentContext
            {
                Id = "package:utilities",
                Name = "utilities",
                QualifiedName = "sample.utilities",
                Language = "Java",
                ComponentKind = "Package",
                ProductionSymbolCount = 1
            }
        ]);
        context.ArchitectureComponentDependencies.Add(
            new ArchitectureComponentDependencyContext
            {
                SourceComponentId = "package:application",
                SourceComponentName = "sample.application",
                TargetComponentId = "package:domain",
                TargetComponentName = "sample.domain",
                RelationshipCount = 2
            });
        context.SemanticAnalysisRuns.Add(
            new SemanticAnalysisRun
            {
                Language = "Java",
                EngineId = "eclipse-jdt",
                Status = SemanticAnalysisStatus.Completed
            });

        var results = await Evaluate(project, "DependencyGraphEvaluator", context);
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("DependencyGraphDensity"));

        Assert.Equal(2, summary.Metrics["TotalRelations"]);
        Assert.Equal(1d / 6d, summary.Metrics["DependencyGraphDensity"], precision: 5);
        Assert.Equal("ArchitecturalComponents", summary.Metadata["GraphLevel"]);
        Assert.Equal("3", summary.Metadata["GraphNodeCount"]);
        Assert.Equal("1", summary.Metadata["GraphEdgeCount"]);
        Assert.Equal("true", summary.Metadata["AnalysisComplete"]);
        Assert.Equal("MeasuredOnObservedEdges", summary.Metadata["GraphDensityStatus"]);
    }

    [Fact]
    public async Task DependencyGraphEvaluator_reports_zero_density_only_when_component_analysis_is_complete()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "Java", "Spring Boot");
        context.ArchitectureComponents.AddRange(
        [
            new ArchitectureComponentContext
            {
                Id = "package:application",
                QualifiedName = "sample.application",
                Language = "Java",
                ComponentKind = "Package",
                ProductionSymbolCount = 2
            },
            new ArchitectureComponentContext
            {
                Id = "package:domain",
                QualifiedName = "sample.domain",
                Language = "Java",
                ComponentKind = "Package",
                ProductionSymbolCount = 2
            }
        ]);
        context.SemanticAnalysisRuns.Add(
            new SemanticAnalysisRun
            {
                Language = "Java",
                EngineId = "eclipse-jdt",
                Status = SemanticAnalysisStatus.Completed
            });

        var results = await Evaluate(project, "DependencyGraphEvaluator", context);
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("TotalRelations"));

        Assert.Equal(0, summary.Metrics["TotalRelations"]);
        Assert.Equal(0, summary.Metrics["DependencyGraphDensity"]);
        Assert.Equal("MeasuredZeroEdgesWithCompleteAnalysis", summary.Metadata["GraphDensityStatus"]);
        Assert.Equal("2", summary.Metadata["GraphNodeCount"]);
        Assert.Equal("0", summary.Metadata["GraphEdgeCount"]);
        Assert.Equal("true", summary.Metadata["AnalysisComplete"]);
    }

    [Fact]
    public async Task DependencyGraphEvaluator_measures_supplied_layer_relations()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "C#", "ASP.NET Core");
        context.Dependencies.AddRange(
        [
            new ArchitectureDependencyContext
            {
                Source = "Api",
                Target = "Orders",
                SourceLayer = "Presentation",
                TargetLayer = "Application",
                DependencyType = "Project"
            },
            new ArchitectureDependencyContext
            {
                Source = "Orders",
                Target = "Database",
                SourceLayer = "Application",
                TargetLayer = "Infrastructure",
                DependencyType = "Project"
            },
            new ArchitectureDependencyContext
            {
                Source = "TestProject",
                Target = "Api",
                SourceLayer = "Tests",
                TargetLayer = "Presentation",
                DependencyType = "Project",
                Scope = "Test"
            }
        ]);

        var results = await Evaluate(project, "DependencyGraphEvaluator", context);
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("TotalRelations"));

        Assert.Equal(2, summary.Metrics["TotalRelations"]);
        Assert.Equal(0, summary.Metrics["RelationsMissingSourceLayer"]);
        Assert.Equal(0, summary.Metrics["RelationsMissingTargetLayer"]);
        Assert.Equal(2d / 6d, summary.Metrics["DependencyGraphDensity"], precision: 5);
        Assert.Equal("Measured", summary.Metadata["GraphDensityStatus"]);
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task CouplingEvaluator_reports_architectural_and_external_dependency_counts()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "C#", "ASP.NET Core");
        context.Dependencies.AddRange(
        [
            new ArchitectureDependencyContext
            {
                Source = "Orders",
                Target = "Database",
                SourceLayer = "Application",
                TargetLayer = "Infrastructure",
                DependencyType = "Project"
            },
            new ArchitectureDependencyContext
            {
                Source = "TestProject",
                Target = "Database",
                SourceLayer = "Tests",
                TargetLayer = "Infrastructure",
                DependencyType = "Project",
                Scope = "Test"
            }
        ]);
        context.DetectedDependencies.AddRange(["Microsoft.EntityFrameworkCore", "Serilog"]);

        var results = await Evaluate(project, "CouplingEvaluator", context);

        var architecturalSummary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("CrossLayerDependencyCount"));
        Assert.Equal(0, architecturalSummary.Metrics["CrossLayerDependencyCount"]);
        Assert.Equal(1, architecturalSummary.Metrics["UnclassifiedDependencyCount"]);
        Assert.Equal(0, architecturalSummary.Metrics["ArchitecturalCouplingClassificationComplete"]);
        Assert.Contains(results, result =>
            result.Metrics.ContainsKey("ExternalDependencyCount")
            && result.Metrics["ExternalDependencyCount"] == 2);
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task RepositoryEvaluator_emits_finite_repository_health_metrics()
    {
        using var project = new TemporaryProject();
        project.Write("README.md", "# Fixture repository");
        project.Write("src/Service.cs", "public sealed class Service { }");
        project.Write(".gitignore", "bin/\nobj/\n");

        var results = await Evaluate(project, "RepositoryEvaluator", "C#", ".NET");

        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("RepositoryHealthIndex"));

        Assert.InRange(summary.Metrics["RepositoryHealthIndex"], 0, 100);
        Assert.True(int.Parse(summary.Metadata["RepositoryFileCount"]) >= 2);
        AssertAllMetricsAreFinite(results);
    }

    private static async Task<ArchitectureEvaluatorResult[]> Evaluate(
        TemporaryProject project,
        string evaluatorName,
        string language,
        string? framework)
    {
        var context = CreateContext(project, language, framework);
        return await Evaluate(project, evaluatorName, context);
    }

    private static async Task<ArchitectureEvaluatorResult[]> Evaluate(
        TemporaryProject project,
        string evaluatorName,
        ProjectArchitectureContext context)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAegisCore();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var evaluator = Assert.Single(
            scope.ServiceProvider.GetServices<IEvaluator>(),
            item => item.Name == evaluatorName);

        var results = (await evaluator.EvaluateAsync(
            project.Root,
            context,
            CancellationToken.None)).ToArray();

        AssertAllMetricsAreFinite(results);
        return results;
    }

    private static ProjectArchitectureContext CreateContext(
        TemporaryProject project,
        string language,
        string? framework) =>
        new()
        {
            ProjectName = "AdditionalEvaluatorFixture",
            RootPath = project.Root,
            Language = language,
            Framework = framework,
            Confidence = 0.95
        };

    private static void AssertAllMetricsAreFinite(
        IEnumerable<ArchitectureEvaluatorResult> results)
    {
        foreach (var result in results)
        {
            Assert.All(result.Metrics, metric =>
                Assert.True(
                    double.IsFinite(metric.Value),
                    $"Metric '{metric.Key}' was not finite: {metric.Value}."));
        }
    }

    private sealed class TemporaryProject : IDisposable
    {
        public string Root { get; } = Path.Combine(
            FindRepositoryRoot(),
            $"AegisAdditionalEvaluator_{Guid.NewGuid():N}");

        public TemporaryProject() => Directory.CreateDirectory(Root);

        public string Write(string relativePath, string content)
        {
            var fullPath = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "sources"))
                    && Directory.Exists(Path.Combine(directory.FullName, "tests")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Aegis repository root for evaluator fixtures.");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
