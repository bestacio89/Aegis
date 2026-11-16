using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinSemanticAnalysisEndToEndTests
{
    [RequiresKotlinWorkerPackage]
    public async Task Analyses_kotlin_source_and_retains_host_owned_jvm_output()
    {
        var packageDirectory = Environment.GetEnvironmentVariable("AEGIS_JAVA_WORKER_PACKAGE")
            ?? throw new InvalidOperationException("The Kotlin worker package path was not configured.");
        var workspace = Directory.CreateTempSubdirectory("aegis-kotlin-e2e-");
        var sourceRoot = Path.Combine(workspace.FullName, "src", "main", "kotlin");
        var sourceFile = Path.Combine(sourceRoot, "sample", "application", "WorkerProbe.kt");
        var domainFile = Path.Combine(sourceRoot, "sample", "domain", "OrderRepository.kt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(domainFile)!);
        await File.WriteAllTextAsync(
            sourceFile,
            """
            package sample.application

            import sample.domain.OrderRepository

            class WorkerProbe(private val repository: OrderRepository) {
                fun execute(id: String): String = repository.findById(id).id
            }
            """);
        await File.WriteAllTextAsync(
            domainFile,
            """
            package sample.domain

            data class Order(val id: String)

            interface OrderRepository {
                fun findById(id: String): Order
            }
            """);

        var springAnnotationsFile = Path.Combine(
            sourceRoot,
            "org",
            "springframework",
            "web",
            "bind",
            "annotation",
            "SpringAnnotations.kt");
        var controllerFile = Path.Combine(sourceRoot, "sample", "application", "OrdersController.kt");
        Directory.CreateDirectory(Path.GetDirectoryName(springAnnotationsFile)!);
        await File.WriteAllTextAsync(
            springAnnotationsFile,
            """
            package org.springframework.web.bind.annotation

            enum class RequestMethod { GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS }

            @Target(AnnotationTarget.CLASS, AnnotationTarget.FUNCTION)
            annotation class RequestMapping(
                val value: String = "",
                val path: String = "",
                val method: Array<RequestMethod> = []
            )

            @Target(AnnotationTarget.FUNCTION)
            annotation class GetMapping(val value: String = "")
            """);
        await File.WriteAllTextAsync(
            controllerFile,
            """
            package sample.application

            import org.springframework.web.bind.annotation.GetMapping
            import org.springframework.web.bind.annotation.RequestMapping

            @RequestMapping("/api/orders")
            class OrdersController {
                @GetMapping("/{id}")
                fun getOrder(id: String): String = id
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<KotlinWorkerOptions>(_ => { });
        services.AddSingleton<IKotlinWorkerClient, KotlinWorkerClient>();
        await using var provider = services.BuildServiceProvider();
        var workerClient = provider.GetRequiredService<IKotlinWorkerClient>();
        var adapter = new KotlinSemanticAnalysisAdapter(
            workerClient,
            NullLogger<KotlinSemanticAnalysisAdapter>.Instance);
        await using var executionContext = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);

        try
        {
            Assert.True(Directory.Exists(packageDirectory));

            var request = new SemanticAnalysisRequest
            {
                Language = "Kotlin",
                ProjectPath = workspace.FullName,
                WorkspaceRoot = workspace.FullName,
                ModuleName = "worker-probe",
                BuildSystem = "Gradle",
                SourceFiles = [sourceFile, domainFile, springAnnotationsFile, controllerFile],
                SourceRoots = [sourceRoot],
                RequestedCapabilities = ["symbols", "relationships", "server-api-endpoints", "compiler-diagnostics", "jvm-interop"],
                ProviderOptions = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["kotlin.compilerVersion"] = "2.4.21",
                    ["kotlin.languageVersion"] = "2.4",
                    ["kotlin.apiVersion"] = "2.4",
                    ["kotlin.jvmTarget"] = "21",
                    ["kotlin.moduleName"] = "worker_probe",
                    ["kotlin.configurationInferred"] = "false"
                }
            };

            var run = await adapter.AnalyzeAsync(request, executionContext);

            Assert.True(
                run.Status == SemanticAnalysisStatus.Completed,
                $"Expected completed Kotlin analysis, received {run.Status}: {string.Join(" | ", run.Diagnostics.Select(diagnostic => $"{diagnostic.Code} ({diagnostic.ExceptionType}): {diagnostic.Message}"))}");
            Assert.Contains(run.Symbols, symbol =>
                symbol.QualifiedName == "sample.application.WorkerProbe"
                && symbol.Language == "Kotlin");
            var endpoint = Assert.Single(run.ServerApiEndpoints);
            Assert.Equal("Kotlin", endpoint.Language);
            Assert.Equal("Spring MVC/WebFlux", endpoint.Framework);
            Assert.Equal("/api/orders/{id}", endpoint.RouteTemplate);
            Assert.Equal(new[] { "GET" }, endpoint.HttpMethods);
            Assert.Equal(workspace.FullName, endpoint.ModulePath);
            var domainSymbolIds = run.Symbols
                .Where(symbol => symbol.ContainerName == "sample.domain")
                .Select(symbol => symbol.Id)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains(run.Relationships, relationship =>
                (relationship.Kind is SemanticRelationshipKind.Calls or SemanticRelationshipKind.References)
                && domainSymbolIds.Contains(relationship.TargetSymbolId ?? string.Empty));

            var context = new ProjectArchitectureContext
            {
                ProjectName = "worker-probe",
                RootPath = workspace.FullName,
                Language = "Kotlin",
                Modules =
                [
                    new ProjectModuleContext
                    {
                        Name = "worker-probe",
                        Path = workspace.FullName,
                        Languages = ["Kotlin"]
                    }
                ],
                SemanticAnalysisRuns = [run]
            };
            SemanticAnalysisPipeline.ProjectArchitectureComponents(context);

            Assert.Equal(2, context.ProductionComponents.Count);
            var relationshipEdge = Assert.Single(context.ProductionComponentDependencies);
            Assert.True(relationshipEdge.RelationshipCount > 0);
            Assert.Equal("sample.application", context.ProductionComponents
                .Single(component => component.Id == relationshipEdge.SourceComponentId).QualifiedName);
            Assert.Equal("sample.domain", context.ProductionComponents
                .Single(component => component.Id == relationshipEdge.TargetComponentId).QualifiedName);

            var outputDirectory = Assert.Single(
                executionContext.GetCompilationOutputs(workspace.FullName, includeTestCode: false));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "sample", "application", "WorkerProbe.class")));
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }
}
