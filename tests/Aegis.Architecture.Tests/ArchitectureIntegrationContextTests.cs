using System.Text.Json;
using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureIntegrationContextTests
{
    [Fact]
    public void Application_integration_store_persists_portable_relative_module_paths()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"AegisIntegrationStore_{Guid.NewGuid():N}");
        var source = Path.Combine(root, "frontend");
        var target = Path.Combine(root, "backend");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);

        try
        {
            var integration = new ArchitectureIntegrationContext
            {
                SourceModuleName = "frontend",
                SourceModulePath = source,
                TargetModuleName = "backend",
                TargetModulePath = target,
                InteractionPattern = "RequestResponse",
                Protocol = "HTTP",
                EvidenceOrigin = "UserDefined"
            };

            ApplicationIntegrationConfigurationStore.Save(root, [integration]);

            var configurationPath =
                ApplicationIntegrationConfigurationStore.GetConfigurationPath(root);
            var storedJson = File.ReadAllText(configurationPath);
            var restored = Assert.Single(
                ApplicationIntegrationConfigurationStore.Load(root));

            Assert.DoesNotContain(root, storedJson, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(source), restored.SourceModulePath);
            Assert.Equal(Path.GetFullPath(target), restored.TargetModulePath);
            Assert.Equal("UserDefined", restored.EvidenceOrigin);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Application_integration_store_rejects_endpoints_outside_repository_root()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"AegisIntegrationStore_{Guid.NewGuid():N}");
        var outside = Path.Combine(
            Path.GetTempPath(),
            $"AegisIntegrationOutside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        try
        {
            var integration = new ArchitectureIntegrationContext
            {
                SourceModuleName = "frontend",
                SourceModulePath = root,
                TargetModuleName = "external",
                TargetModulePath = outside
            };

            Assert.Throws<InvalidDataException>(() =>
                ApplicationIntegrationConfigurationStore.Save(root, [integration]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

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
