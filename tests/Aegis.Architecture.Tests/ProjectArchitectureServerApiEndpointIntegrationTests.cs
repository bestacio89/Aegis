using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ProjectArchitectureServerApiEndpointIntegrationTests
{
    [Fact]
    public void Detect_discovers_node_module_server_endpoints_into_the_shared_registry()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "package.json"),
            """
            {
              "name": "orders-api",
              "private": true,
              "dependencies": { "express": "^5.0.0" }
            }
            """);

        var routes = Path.Combine(directory.Path, "routes.ts");
        File.WriteAllText(
            routes,
            """
            import express from "express";
            const app = express();
            app.get("/orders", getOrders);
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        var module = Assert.Single(context.Modules);
        Assert.Contains("TypeScript", module.Languages);
        var registry = Assert.IsType<ServerApiEndpointDiscoveryRegistry>(context.ServerApiEndpointDiscoveryRegistry);
        var endpoint = Assert.Single(registry.Evidence);
        Assert.Equal("TypeScript", endpoint.Language);
        Assert.Equal("Express", endpoint.Framework);
        Assert.Equal(directory.Path, endpoint.ModulePath);
        Assert.Equal(routes, endpoint.SourceFile);
        Assert.Equal("/orders", endpoint.RouteTemplate);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "aegis-server-api-integration",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
