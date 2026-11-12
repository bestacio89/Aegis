using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace Aegis.Architecture.Tests;

public sealed class JavaSemanticAnalysisEndToEndTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Packaged_jdt_worker_produces_semantic_evidence_through_the_production_orchestrator()
    {
        var packageRoot = Environment.GetEnvironmentVariable("AEGIS_JAVA_WORKER_PACKAGE");
        if (string.IsNullOrWhiteSpace(packageRoot))
            throw new SkipException("Set AEGIS_JAVA_WORKER_PACKAGE to the packaged Java worker directory to run this integration test.");

        packageRoot = Path.GetFullPath(packageRoot);
        var javaExecutable = Path.Combine(
            packageRoot,
            "runtime",
            "bin",
            OperatingSystem.IsWindows() ? "java.exe" : "java");
        var workerClasses = Path.Combine(packageRoot, "worker", "classes");
        var workerLibraries = Path.Combine(packageRoot, "worker", "lib");

        if (!File.Exists(javaExecutable)
            || !File.Exists(Path.Combine(workerClasses, "org", "aegis", "worker", "WorkerMain.class"))
            || !File.Exists(Path.Combine(workerLibraries, "org.eclipse.jdt.core-3.47.0.jar")))
        {
            throw new SkipException("The packaged Eclipse JDT worker is unavailable; run Package-Worker.ps1 before this integration test.");
        }

        var workspace = Path.Combine(Path.GetTempPath(), $"aegis-jdt-e2e-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(workspace, "src", "main", "java", "sample");
        Directory.CreateDirectory(sourceRoot);

        await File.WriteAllTextAsync(
            Path.Combine(sourceRoot, "Order.java"),
            """
            package sample;

            public record Order(String id) {}
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sourceRoot, "OrderRepository.java"),
            """
            package sample;

            public interface OrderRepository
            {
                Order findById(String id);
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sourceRoot, "OrderService.java"),
            """
            package sample;

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
                        Directory = Path.GetDirectoryName(sourceRoot)!,
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
                var orchestrator = provider.GetRequiredService<JavaSemanticAnalysisOrchestrator>();
                await orchestrator.AnalyzeAsync(context);

                var run = Assert.Single(context.SemanticAnalysisRuns);
                Assert.Equal("eclipse-jdt", run.EngineId);
                Assert.Equal("3.47.0", run.EngineVersion);
                Assert.Equal(SemanticAnalysisStatus.Completed, run.Status);
                Assert.Equal(3, run.AnalyzedFileCount);
                Assert.NotEmpty(run.Symbols);
                Assert.NotEmpty(run.Relationships);
                Assert.DoesNotContain(run.Diagnostics, diagnostic => diagnostic.AffectsCoverage);
            }
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
