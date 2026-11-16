using Aegis.Architecture.Analysis.Api;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ServerApiEndpointDiscoveryTests
{
    [Fact]
    public void DiscoversControllerRouteCompositionAndHttpMethods()
    {
        using var fixture = new SourceFixture("""
            using Microsoft.AspNetCore.Mvc;
            [ApiController]
            [Route("api/[controller]")]
            public sealed class OrdersController : ControllerBase
            {
                [HttpGet("{id:guid}")]
                public IActionResult Get(Guid id) => Ok();
                [HttpPost]
                public IActionResult Create() => Ok();
            }
            """);

        var endpoints = new CSharpAspNetEndpointProvider().Discover([fixture.FilePath], fixture.ModulePath);
        Assert.Equal(2, endpoints.Count);
        Assert.Contains(endpoints, endpoint => endpoint.HttpMethods.SequenceEqual(["GET"])
            && endpoint.RouteTemplate == "api/[controller]/{id:guid}"
            && endpoint.ResolutionStatus == ApiEndpointResolutionStatus.PartiallyResolved);
        Assert.Contains(endpoints, endpoint => endpoint.HttpMethods.SequenceEqual(["POST"])
            && endpoint.RouteTemplate == "api/[controller]"
            && endpoint.ResolutionStatus == ApiEndpointResolutionStatus.PartiallyResolved);
    }

    [Fact]
    public void DiscoversMinimalApiLiteralRoutesAndGroupPrefix()
    {
        using var fixture = new SourceFixture("""
            var api = app.MapGroup("/api/v1");
            api.MapGet("/orders/{id}", GetOrder);
            app.MapPost("/orders", CreateOrder);
            """);

        var endpoints = new CSharpAspNetEndpointProvider().Discover([fixture.FilePath], fixture.ModulePath);
        Assert.Equal(2, endpoints.Count);
        Assert.Contains(endpoints, endpoint => endpoint.HttpMethods.SequenceEqual(["GET"])
            && endpoint.RouteTemplate == "/api/v1/orders/{id}");
        Assert.Contains(endpoints, endpoint => endpoint.HttpMethods.SequenceEqual(["POST"])
            && endpoint.RouteTemplate == "/orders");
    }

    [Fact]
    public void RetainsDynamicRoutesInsteadOfInventingLiteralTemplates()
    {
        using var fixture = new SourceFixture("""
            var route = configuration["OrdersRoute"];
            app.MapGet(route, GetOrders);
            """);

        var endpoint = Assert.Single(new CSharpAspNetEndpointProvider().Discover([fixture.FilePath], fixture.ModulePath));
        Assert.Equal("configuration[\"OrdersRoute\"]", endpoint.RouteTemplate);
        Assert.Equal(ApiEndpointResolutionStatus.Dynamic, endpoint.ResolutionStatus);
    }

    [Fact]
    public void RegistryPreservesDuplicateRoutesAndUnresolvedEvidence()
    {
        var first = new ApiEndpointEvidence { EvidenceId = "first", ModulePath = "/repo/api", RouteTemplate = "/orders", HttpMethods = ["GET"], ResolutionStatus = ApiEndpointResolutionStatus.Resolved };
        var second = new ApiEndpointEvidence { EvidenceId = "second", ModulePath = "/repo/api", RouteTemplate = "/orders", HttpMethods = ["GET"], ResolutionStatus = ApiEndpointResolutionStatus.Dynamic };
        var registry = new ServerApiEndpointDiscoveryRegistry([first, second]);

        Assert.Equal(2, registry.Count);
        Assert.Equal(2, registry.ForModule("/repo/api").Count);
        Assert.Single(registry.ForStatus(ApiEndpointResolutionStatus.Dynamic));
    }

    private sealed class SourceFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "aegis-api-endpoints", Guid.NewGuid().ToString("N"));
        public SourceFixture(string source)
        {
            Directory.CreateDirectory(_directory);
            ModulePath = _directory;
            FilePath = Path.Combine(_directory, "Endpoints.cs");
            File.WriteAllText(FilePath, source);
        }
        public string ModulePath { get; }
        public string FilePath { get; }
        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
