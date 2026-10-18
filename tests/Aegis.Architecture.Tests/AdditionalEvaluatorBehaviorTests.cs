using Aegis.Architecture.Bootstrap;
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
            result => result.Metrics.ContainsKey("ApiConsistencyIndex"));

        Assert.True(fileResult.Metrics["UppercaseRouteViolations"] >= 1);
        Assert.True(fileResult.Metrics["TrailingSlashViolations"] >= 1);
        Assert.True(fileResult.Metrics["MissingVersionViolations"] >= 1);
        Assert.Equal(100, fileResult.Metrics["EndpointDiscoveryScore"]);
        Assert.Contains(results, result =>
            result.Metadata.GetValueOrDefault("EvaluationScope") == "Project"
            && result.Metrics.ContainsKey("ApiMaturityIndex"));
        AssertAllMetricsAreFinite(results);
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
            }
        ]);

        var results = await Evaluate(project, "CircularDependencyEvaluator", context);

        Assert.Contains(results, result =>
            result.Metrics.GetValueOrDefault("HasCycle") == 1
            && result.Metrics.GetValueOrDefault("CircularDependencyViolation") == 1);
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("CircularDependencyCount"));
        Assert.True(summary.Metrics["CircularDependencyCount"] >= 1);
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
        project.Write("package.json", """
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
            result => result.Metrics.ContainsKey("DeclaredReferences"));

        Assert.True(referenceSummary.Metrics["DeclaredReferences"] >= 3);
        Assert.Equal(bool.FalseString, referenceSummary.Metadata["UnusedReferencesDetermined"]);
        AssertAllMetricsAreFinite(versionResults);
        AssertAllMetricsAreFinite(referenceResults);
    }

    [Fact]
    public async Task CohesionEvaluator_reports_structural_measurements_for_a_type()
    {
        using var project = new TemporaryProject();
        project.Write("back/OrderProcessor.cs", """
            public sealed class OrderProcessor
            {
                private int _count;
                private string _name = string.Empty;

                public void Process() { _count++; }
                public void Rename(string name) { _name = name; }
            }
            """);

        var results = await Evaluate(project, "CohesionEvaluator", "C#", "ASP.NET Core");
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

        Assert.Contains(results, result =>
            result.Metrics.ContainsKey("DecisionPointCount")
            && result.Metrics.ContainsKey("CommentDensity"));
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("AnalyzedFiles"));

        Assert.True(summary.Metrics["AnalyzedFiles"] >= 1);
        Assert.True(summary.Metrics["TotalNonEmptyLines"] > 0);
        AssertAllMetricsAreFinite(results);
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
            }
        ]);

        var results = await Evaluate(project, "DependencyGraphEvaluator", context);
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("TotalRelations"));

        Assert.Equal(2, summary.Metrics["TotalRelations"]);
        Assert.Equal(0, summary.Metrics["RelationsMissingSourceLayer"]);
        Assert.Equal(0, summary.Metrics["RelationsMissingTargetLayer"]);
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task CouplingEvaluator_reports_architectural_and_external_dependency_counts()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "C#", "ASP.NET Core");
        context.Dependencies.Add(
            new ArchitectureDependencyContext
            {
                Source = "Orders",
                Target = "Database",
                SourceLayer = "Application",
                TargetLayer = "Infrastructure",
                DependencyType = "Project"
            });
        context.DetectedDependencies.AddRange(["Microsoft.EntityFrameworkCore", "Serilog"]);

        var results = await Evaluate(project, "CouplingEvaluator", context);

        Assert.Contains(results, result =>
            result.Metrics.ContainsKey("CrossLayerDependencyCount")
            && result.Metrics["CrossLayerDependencyCount"] >= 1);
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
        Assert.True(summary.Metrics["RepositoryFileCount"] >= 2);
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
