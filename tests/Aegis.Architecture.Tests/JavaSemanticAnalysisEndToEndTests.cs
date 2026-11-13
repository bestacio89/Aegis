using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JavaSemanticAnalysisEndToEndTests
{
    [RequiresJavaWorkerPackage]
    [Trait("Category", "Integration")]
    public async Task Packaged_jdt_worker_produces_semantic_evidence_through_the_production_orchestrator()
    {
        var packageRoot = Path.GetFullPath(
            Environment.GetEnvironmentVariable("AEGIS_JAVA_WORKER_PACKAGE")!);
        var javaExecutable = Path.Combine(
            packageRoot,
            "runtime",
            "bin",
            OperatingSystem.IsWindows() ? "java.exe" : "java");
        var workerClasses = Path.Combine(packageRoot, "worker", "classes");
        var workerLibraries = Path.Combine(packageRoot, "worker", "lib");

        Assert.True(File.Exists(javaExecutable), $"Private Java runtime not found: {javaExecutable}");
        Assert.True(
            File.Exists(Path.Combine(workerClasses, "org", "aegis", "worker", "WorkerMain.class")),
            "Packaged Java worker classes were not found.");
        Assert.True(
            File.Exists(Path.Combine(workerLibraries, "org.eclipse.jdt.core-3.47.0.jar")),
            "Packaged Eclipse JDT dependency was not found.");

        var workspace = Path.Combine(Path.GetTempPath(), $"aegis-jdt-e2e-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(workspace, "src", "main", "java");
        var domainRoot = Path.Combine(sourceRoot, "sample", "domain");
        var applicationRoot = Path.Combine(sourceRoot, "sample", "application");
        Directory.CreateDirectory(domainRoot);
        Directory.CreateDirectory(applicationRoot);

        await File.WriteAllTextAsync(
            Path.Combine(domainRoot, "Order.java"),
            """
            package sample.domain;

            public record Order(String id) {}
            """);
        await File.WriteAllTextAsync(
            Path.Combine(domainRoot, "OrderRepository.java"),
            """
            package sample.domain;

            public interface OrderRepository
            {
                Order findById(String id);
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(applicationRoot, "OrderService.java"),
            """
            package sample.application;

            import sample.domain.Order;
            import sample.domain.OrderRepository;

            public final class OrderService
            {
                private final OrderRepository repository;

                public OrderService(OrderRepository repository)
                {
                    this.repository = repository;
                }

                public Order find(String id)
                {
                    return repository.findById(id);
                }
            }
            """);

        try
        {
            var module = new ProjectModuleContext
            {
                Name = "sample",
                Path = workspace,
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
                ProjectName = "jdt-end-to-end",
                RootPath = workspace,
                Language = "Java",
                Modules = [module]
            };

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddJavaSemanticAnalysis();
            services.Configure<JavaWorkerOptions>(options =>
            {
                options.JavaExecutablePath = javaExecutable;
                options.WorkerClassesDirectory = workerClasses;
                options.WorkerLibrariesDirectory = workerLibraries;
                options.SourceLevel = "21";
            });

            await using (var provider = services.BuildServiceProvider())
            {
                // Exercise the same provider-neutral semantic pipeline used by
                // AegisArchitectureAnalyzer. The Java orchestrator produces raw
                // JDT evidence; the pipeline projects that evidence into the
                // language-native architectural component graph.
                var pipeline = provider.GetRequiredService<SemanticAnalysisPipeline>();
                await pipeline.AnalyzeAsync(context);

                var run = Assert.Single(context.SemanticAnalysisRuns);
                Assert.Equal("eclipse-jdt", run.EngineId);
                Assert.Equal("3.47.0", run.EngineVersion);
                Assert.Equal(SemanticAnalysisStatus.Completed, run.Status);
                Assert.Equal(3, run.AnalyzedFileCount);
                Assert.NotEmpty(run.Symbols);
                Assert.NotEmpty(run.Relationships);
                Assert.DoesNotContain(run.Diagnostics, diagnostic => diagnostic.AffectsCoverage);

                Assert.Equal(2, context.ArchitectureComponents.Count);
                var packageDependency = Assert.Single(context.ProductionComponentDependencies);
                Assert.Equal("sample.application", context.ArchitectureComponents
                    .Single(component => component.Id == packageDependency.SourceComponentId).QualifiedName);
                Assert.Equal("sample.domain", context.ArchitectureComponents
                    .Single(component => component.Id == packageDependency.TargetComponentId).QualifiedName);
                Assert.True(packageDependency.RelationshipCount > 0);
                Assert.Empty(context.TestComponentDependencies);
            }
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
