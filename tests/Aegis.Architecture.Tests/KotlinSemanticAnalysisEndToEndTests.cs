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

        var javaSourceRoot = Path.Combine(workspace.FullName, "src", "main", "java");
        var springAnnotationDirectory = Path.Combine(
            javaSourceRoot,
            "org",
            "springframework",
            "web",
            "bind",
            "annotation");
        var requestMethodFile = Path.Combine(springAnnotationDirectory, "RequestMethod.java");
        var requestMappingFile = Path.Combine(springAnnotationDirectory, "RequestMapping.java");
        var getMappingFile = Path.Combine(springAnnotationDirectory, "GetMapping.java");
        var controllerFile = Path.Combine(sourceRoot, "sample", "application", "OrdersController.kt");
        var ktorClientFile = Path.Combine(sourceRoot, "io", "ktor", "client", "HttpClient.kt");
        var ktorRequestsFile = Path.Combine(sourceRoot, "io", "ktor", "client", "request", "Requests.kt");
        var apiGatewayFile = Path.Combine(sourceRoot, "sample", "application", "KotlinApiGateway.kt");
        Directory.CreateDirectory(springAnnotationDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(ktorClientFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ktorRequestsFile)!);
        await File.WriteAllTextAsync(
            requestMethodFile,
            """
            package org.springframework.web.bind.annotation;

            public enum RequestMethod {
                GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS
            }
            """);
        await File.WriteAllTextAsync(
            requestMappingFile,
            """
            package org.springframework.web.bind.annotation;

            import java.lang.annotation.ElementType;
            import java.lang.annotation.Retention;
            import java.lang.annotation.RetentionPolicy;
            import java.lang.annotation.Target;

            @Retention(RetentionPolicy.RUNTIME)
            @Target({ElementType.TYPE, ElementType.METHOD})
            public @interface RequestMapping {
                String[] value() default {};
                String[] path() default {};
                RequestMethod[] method() default {};
            }
            """);
        await File.WriteAllTextAsync(
            getMappingFile,
            """
            package org.springframework.web.bind.annotation;

            import java.lang.annotation.ElementType;
            import java.lang.annotation.Retention;
            import java.lang.annotation.RetentionPolicy;
            import java.lang.annotation.Target;

            @Retention(RetentionPolicy.RUNTIME)
            @Target(ElementType.METHOD)
            public @interface GetMapping {
                String[] value() default {};
                String[] path() default {};
            }
            """);
        await File.WriteAllTextAsync(
            ktorClientFile,
            """
            package io.ktor.client

            class HttpClient
            """);
        await File.WriteAllTextAsync(
            ktorRequestsFile,
            """
            package io.ktor.client.request

            import io.ktor.client.HttpClient

            suspend fun HttpClient.get(urlString: String): String = urlString
            """);
        await File.WriteAllTextAsync(
            apiGatewayFile,
            """
            package sample.application

            import io.ktor.client.HttpClient
            import io.ktor.client.request.get

            class KotlinApiGateway(private val client: HttpClient) {
                suspend fun fetch(orderId: String): String = client.get("/api/orders/$orderId")
            }
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
                SourceFiles = [sourceFile, domainFile, controllerFile, ktorClientFile, ktorRequestsFile, apiGatewayFile],
                SourceRoots = [sourceRoot],
                ReferencedSourceRoots = [javaSourceRoot],
                RequestedCapabilities = ["symbols", "relationships", "server-api-endpoints", "api-client-requests", "compiler-diagnostics", "jvm-interop"],
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
            Assert.Contains("api-client-requests", run.Capabilities);
            var clientRequest = Assert.Single(run.ApiClientRequests);
            Assert.Equal("Kotlin", clientRequest.Language);
            Assert.Equal("Ktor Client", clientRequest.Framework);
            Assert.Equal("GET", clientRequest.HttpMethod);
            Assert.Contains("orderId", clientRequest.RequestUriExpression, StringComparison.Ordinal);
            Assert.Equal(ApiClientRequestResolutionStatus.PartiallyResolved, clientRequest.ResolutionStatus);
            Assert.Equal(workspace.FullName, clientRequest.ModulePath);
            Assert.Contains("KotlinApiGateway.fetch", clientRequest.CallerIdentity);
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
