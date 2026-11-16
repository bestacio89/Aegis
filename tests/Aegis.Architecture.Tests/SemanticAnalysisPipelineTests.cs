using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class SemanticAnalysisPipelineTests
{
    [Fact]
    public async Task AnalyzeAsync_executes_registered_language_analyzers_in_deterministic_order()
    {
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
        [
            new RecordingAnalyzer("Python", executionOrder),
            new RecordingAnalyzer("Java", executionOrder),
            new RecordingAnalyzer("C#", executionOrder)
        ],
        NullLogger<SemanticAnalysisPipeline>.Instance);

        var context = new ProjectArchitectureContext
        {
            ProjectName = "Polyglot",
            RootPath = Path.GetTempPath()
        };

        await pipeline.AnalyzeAsync(context);

        Assert.Equal(["C#", "Java", "Python"], executionOrder);
        Assert.Equal(3, context.SemanticAnalysisRuns.Count);
        Assert.All(context.SemanticAnalysisRuns, run =>
            Assert.Equal(SemanticAnalysisStatus.Completed, run.Status));
    }


    [Fact]
    public async Task AnalyzeAsync_merges_language_native_endpoint_observations_into_the_shared_registry()
    {
        var nativeEndpoint = new ApiEndpointEvidence
        {
            EvidenceId = "java-endpoint-1",
            Language = "Java",
            Framework = "Spring MVC/WebFlux",
            ProviderId = "eclipse-jdt-spring",
            ModulePath = Path.Combine(Path.GetTempPath(), "java-module"),
            SourceFile = Path.Combine(Path.GetTempPath(), "java-module", "OrdersController.java"),
            StartLine = 12,
            StartColumn = 4,
            HandlerIdentity = "example.OrdersController#getOrder()",
            HttpMethods = ["GET"],
            RouteTemplate = "/api/orders/{id}",
            ResolutionStatus = ApiEndpointResolutionStatus.Resolved,
            RouteCompositionEvidence = ["@RequestMapping(\"/api/orders\")", "@GetMapping(\"/{id}\")"],
            DeclarationEvidence = "public Order getOrder()"
        };
        var csharpEndpoint = new ApiEndpointEvidence
        {
            EvidenceId = "csharp-endpoint-1",
            Language = "C#",
            Framework = "ASP.NET Core",
            ProviderId = "roslyn-aspnet",
            ModulePath = Path.Combine(Path.GetTempPath(), "api-module"),
            SourceFile = Path.Combine(Path.GetTempPath(), "api-module", "OrdersController.cs"),
            HandlerIdentity = "OrdersController.Get",
            HttpMethods = ["GET"],
            RouteTemplate = "/api/orders",
            ResolutionStatus = ApiEndpointResolutionStatus.Resolved
        };
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
            [new RecordingAnalyzer("Java", executionOrder, serverApiEndpoints: [nativeEndpoint])],
            NullLogger<SemanticAnalysisPipeline>.Instance);
        var context = new ProjectArchitectureContext
        {
            ProjectName = "Polyglot",
            RootPath = Path.GetTempPath(),
            ServerApiEndpointDiscoveryRegistry = new ServerApiEndpointDiscoveryRegistry([csharpEndpoint])
        };

        await pipeline.AnalyzeAsync(context);

        Assert.Equal(2, context.ServerApiEndpointDiscoveryRegistry!.Count);
        Assert.Contains(csharpEndpoint, context.ServerApiEndpointDiscoveryRegistry.Evidence);
        Assert.Contains(nativeEndpoint, context.ServerApiEndpointDiscoveryRegistry.Evidence);
    }

    [Fact]
    public async Task AnalyzeAsync_merges_native_client_requests_without_deduplicating_provider_occurrences()
    {
        var existingCSharpRequest = new ApiClientRequestEvidence
        {
            EvidenceId = "csharp-request-1",
            Language = "C#",
            ProviderId = "roslyn-httpclient",
            ModulePath = Path.Combine(Path.GetTempPath(), "csharp-api"),
            HttpMethod = "GET",
            RequestUriExpression = "\"/api/orders\"",
            ResolutionStatus = ApiClientRequestResolutionStatus.Resolved
        };
        var javaRequest = new ApiClientRequestEvidence
        {
            EvidenceId = "provider-local-request-id",
            Language = "Java",
            ProviderId = "eclipse-jdt-spring-client",
            ModulePath = Path.Combine(Path.GetTempPath(), "java-api"),
            HttpMethod = "GET",
            RequestUriExpression = "\"/api/orders\"",
            ResolutionStatus = ApiClientRequestResolutionStatus.Resolved
        };
        var kotlinRequest = new ApiClientRequestEvidence
        {
            EvidenceId = "provider-local-request-id",
            Language = "Kotlin",
            ProviderId = "kotlin-k2-spring-client",
            ModulePath = Path.Combine(Path.GetTempPath(), "kotlin-api"),
            HttpMethod = "GET",
            RequestUriExpression = "ordersEndpoint",
            ResolutionStatus = ApiClientRequestResolutionStatus.Dynamic
        };
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
        [
            new RecordingAnalyzer("Kotlin", executionOrder, apiClientRequests: [kotlinRequest]),
            new RecordingAnalyzer("Java", executionOrder, apiClientRequests: [javaRequest])
        ],
        NullLogger<SemanticAnalysisPipeline>.Instance);
        var context = new ProjectArchitectureContext
        {
            ProjectName = "Polyglot",
            RootPath = Path.GetTempPath(),
            ApiClientRequestDiscoveryRegistry =
                new ApiClientRequestDiscoveryRegistry([existingCSharpRequest])
        };

        await pipeline.AnalyzeAsync(context);

        Assert.Equal(2, context.SemanticAnalysisRuns.Count);
        Assert.Equal(2, context.SemanticAnalysisRuns.Sum(run => run.ApiClientRequests.Count));
        var registry = context.ApiClientRequestDiscoveryRegistry!;
        Assert.Equal(3, registry.Count);
        Assert.Equal(["C#", "Java", "Kotlin"], registry.Evidence.Select(item => item.Language));
        Assert.Equal(2, registry.Evidence.Count(item => item.EvidenceId == "provider-local-request-id"));
        Assert.Single(registry.ForStatus(ApiClientRequestResolutionStatus.Dynamic));
        Assert.Contains(existingCSharpRequest, registry.Evidence);
        Assert.Contains(javaRequest, registry.Evidence);
        Assert.Contains(kotlinRequest, registry.Evidence);
    }

    [Fact]
    public async Task AnalyzeAsync_respects_explicit_execution_order_for_jvm_interoperability()
    {
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
        [
            new RecordingAnalyzer("Java", executionOrder, executionOrderValue: 200),
            new RecordingAnalyzer("Kotlin", executionOrder, executionOrderValue: 100)
        ],
        NullLogger<SemanticAnalysisPipeline>.Instance);

        var context = new ProjectArchitectureContext
        {
            ProjectName = "MixedJvm",
            RootPath = Path.GetTempPath()
        };

        await pipeline.AnalyzeAsync(context);

        Assert.Equal(["Kotlin", "Java"], executionOrder);
    }

    [Fact]
    public async Task AnalyzeAsync_isolates_provider_failures_and_continues_with_other_languages()
    {
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
        [
            new RecordingAnalyzer("Python", executionOrder),
            new RecordingAnalyzer("Java", executionOrder, shouldFail: true)
        ],
        NullLogger<SemanticAnalysisPipeline>.Instance);

        var context = new ProjectArchitectureContext
        {
            ProjectName = "Polyglot",
            RootPath = Path.GetTempPath()
        };

        await pipeline.AnalyzeAsync(context);

        Assert.Equal(["Java", "Python"], executionOrder);
        Assert.Equal(2, context.SemanticAnalysisRuns.Count);

        var failedRun = Assert.Single(context.SemanticAnalysisRuns, run =>
            run.Language == "Java");
        Assert.Equal(SemanticAnalysisStatus.Failed, failedRun.Status);
        Assert.Contains(failedRun.Diagnostics, diagnostic =>
            diagnostic.Code == "SemanticProjectAnalysisFailed"
            && diagnostic.AffectsCoverage);

        var completedRun = Assert.Single(context.SemanticAnalysisRuns, run =>
            run.Language == "Python");
        Assert.Equal(SemanticAnalysisStatus.Completed, completedRun.Status);
    }


    [Fact]
    public async Task AnalyzeAsync_records_missing_semantic_provider_for_discovered_production_language()
    {
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
            [new RecordingAnalyzer("Java", executionOrder)],
            NullLogger<SemanticAnalysisPipeline>.Instance);
        var context = new ProjectArchitectureContext
        {
            ProjectName = "Polyglot",
            RootPath = Path.GetTempPath(),
            Language = "Java",
            Modules =
            [
                new ProjectModuleContext
                {
                    Name = "JavaModule",
                    Path = Path.Combine(Path.GetTempPath(), "java"),
                    Languages = ["Java"]
                },
                new ProjectModuleContext
                {
                    Name = "PythonModule",
                    Path = Path.Combine(Path.GetTempPath(), "python"),
                    Languages = ["Python"]
                }
            ]
        };

        await pipeline.AnalyzeAsync(context);

        var missingRun = Assert.Single(context.SemanticAnalysisRuns, run =>
            run.Language == "Python");
        Assert.Equal(SemanticAnalysisStatus.Unsupported, missingRun.Status);
        Assert.Contains(missingRun.Diagnostics, diagnostic =>
            diagnostic.Code == "SemanticProjectAnalyzerNotRegistered"
            && diagnostic.AffectsCoverage);
    }

    [Fact]
    public async Task AnalyzeAsync_does_not_treat_the_multi_language_repository_label_as_a_language()
    {
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
            [new RecordingAnalyzer("Kotlin", executionOrder)],
            NullLogger<SemanticAnalysisPipeline>.Instance);
        var context = new ProjectArchitectureContext
        {
            ProjectName = "Polyglot",
            RootPath = Path.GetTempPath(),
            Language = "Multi-Language",
            Modules =
            [
                new ProjectModuleContext
                {
                    Name = "kotlin-module",
                    Path = Path.Combine(Path.GetTempPath(), "kotlin-module"),
                    Languages = ["Kotlin"]
                },
                new ProjectModuleContext
                {
                    Name = "frontend",
                    Path = Path.Combine(Path.GetTempPath(), "frontend"),
                    Languages = ["JavaScript"]
                }
            ]
        };

        await pipeline.AnalyzeAsync(context);

        Assert.Equal(["Kotlin"], executionOrder);
        Assert.DoesNotContain(context.SemanticAnalysisRuns, run =>
            run.Language.Equals("Multi-Language", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.SemanticAnalysisRuns, run =>
            run.Language == "JavaScript"
            && run.Status == SemanticAnalysisStatus.Unsupported);
    }

    [Fact]
    public async Task AnalyzeAsync_propagates_cancellation_before_starting_a_provider()
    {
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
            [new RecordingAnalyzer("Java", executionOrder)],
            NullLogger<SemanticAnalysisPipeline>.Instance);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.AnalyzeAsync(
                new ProjectArchitectureContext
                {
                    ProjectName = "Polyglot",
                    RootPath = Path.GetTempPath()
                },
                cancellation.Token));

        Assert.Empty(executionOrder);
    }

    private sealed class RecordingAnalyzer(
        string language,
        ICollection<string> executionOrder,
        bool shouldFail = false,
        int executionOrderValue = 0,
        IReadOnlyList<ApiEndpointEvidence>? serverApiEndpoints = null,
        IReadOnlyList<ApiClientRequestEvidence>? apiClientRequests = null) : ISemanticAnalysisProjectAnalyzer
    {
        public string Language => language;
        public int ExecutionOrder => executionOrderValue;

        public Task AnalyzeAsync(
            ProjectArchitectureContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            executionOrder.Add(language);

            if (shouldFail)
                throw new InvalidOperationException($"Failure from {language} provider.");

            context.SemanticAnalysisRuns.Add(new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = language,
                EngineId = $"{language}-test-provider",
                ProjectPath = context.RootPath,
                ServerApiEndpoints = serverApiEndpoints?.ToList() ?? [],
                ApiClientRequests = apiClientRequests?.ToList() ?? [],
                Status = SemanticAnalysisStatus.Completed,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });

            return Task.CompletedTask;
        }
    }
}
