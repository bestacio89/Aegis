using Aegis.Sdk;
using Aegis.Sdk.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aegis.Sdk.Tests;

public sealed class PluginRegistrarTests
{
    [Fact]
    public void AddAegisPlugins_registers_plugins_with_scoped_lifetime()
    {
        var services = new ServiceCollection();

        var returnedServices = services.AddAegisPlugins(
            assemblyFilter: assembly => assembly == typeof(PluginRegistrarTests).Assembly);

        Assert.Same(services, returnedServices);

        var pluginDescriptors = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(IPlugin) &&
                descriptor.ImplementationType == typeof(TestPlugin))
            .ToArray();

        Assert.NotEmpty(pluginDescriptors);
        Assert.All(pluginDescriptors, descriptor =>
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstPlugin = firstScope.ServiceProvider.GetRequiredService<IPlugin>();
        var sameScopePlugin = firstScope.ServiceProvider.GetRequiredService<IPlugin>();
        var secondScopePlugin = secondScope.ServiceProvider.GetRequiredService<IPlugin>();

        Assert.Same(firstPlugin, sameScopePlugin);
        Assert.NotSame(firstPlugin, secondScopePlugin);
    }

    [Fact]
    public void LoadExternalPlugins_returns_without_throwing_when_directory_is_missing()
    {
        var missingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"aegis-missing-plugins-{Guid.NewGuid():N}");

        Assert.False(Directory.Exists(missingDirectory));
        var exception = Record.Exception(() => PluginRegistrar.LoadExternalPlugins(missingDirectory));

        Assert.Null(exception);
    }

    public sealed class TestPlugin : IPlugin
    {
        public void Initialize()
        {
        }

        public void Shutdown()
        {
        }
    }
}