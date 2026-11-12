using System.Text.Json;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureIntegrationContextTests
{
    [Fact]
    public void Application_integrations_round_trip_without_becoming_build_dependencies()
    {
        var context = new ProjectArchitectureContext
        {
            ProjectName = "PolyglotService",
            ApplicationIntegrations =
            [
                new ArchitectureIntegrationContext
                {
                    SourceModuleName = "web-client",
                    SourceModulePath = "/workspace/web-client",
                    TargetModuleName = "orders-api",
                    TargetModulePath = "/workspace/orders-api",
                    InteractionPattern = "RequestResponse",
                    Protocol = "HTTP",
                    EvidenceOrigin = "UserDefined",
                    Notes = "Angular HttpClient consumes the ASP.NET Core orders API."
                }
            ]
        };

        var json = JsonSerializer.Serialize(context);
        var restored = JsonSerializer.Deserialize<ProjectArchitectureContext>(json);

        var integration = Assert.Single(Assert.IsType<ProjectArchitectureContext>(restored).ApplicationIntegrations);

        Assert.Equal("web-client", integration.SourceModuleName);
        Assert.Equal("orders-api", integration.TargetModuleName);
        Assert.Equal("RequestResponse", integration.InteractionPattern);
        Assert.Equal("HTTP", integration.Protocol);
        Assert.Equal("UserDefined", integration.EvidenceOrigin);
        Assert.Empty(restored!.Dependencies);
    }
}
