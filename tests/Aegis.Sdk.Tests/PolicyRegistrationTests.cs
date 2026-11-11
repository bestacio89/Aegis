using Aegis.SDK.Extensions;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aegis.Sdk.Tests;

public sealed class PolicyRegistrationTests
{
    [Fact]
    public void AddAegisPolicies_binds_root_architecture_document_to_aggregate_policy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EnforceLayerBoundaries"] = "true",
                ["AllowedDependencies:Api:0"] = "Application",
                ["DesignPatterns:EnforceCommandPattern"] = "true",
                ["DesignPatterns:EnforceStrategyPattern"] = "true",
                ["DesignPatterns:EnforceObserverPattern"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAegisPolicies(configuration);

        using var provider = services.BuildServiceProvider();

        var aggregatePolicy = provider
            .GetRequiredService<IOptions<AegisArchitecturePolicy>>()
            .Value;
        var architecturePolicy = provider
            .GetRequiredService<IOptions<ArchitecturePolicy>>()
            .Value;
        var designPatternPolicy = provider
            .GetRequiredService<IOptions<DesignPatternPolicy>>()
            .Value;

        Assert.True(aggregatePolicy.Architecture.EnforceLayerBoundaries);
        Assert.Contains(
            "Application",
            aggregatePolicy.Architecture.AllowedDependencies["Api"]);
        Assert.True(aggregatePolicy.Architecture.DesignPatterns.EnforceCommandPattern);
        Assert.True(aggregatePolicy.Architecture.DesignPatterns.EnforceStrategyPattern);
        Assert.True(aggregatePolicy.Architecture.DesignPatterns.EnforceObserverPattern);

        Assert.True(architecturePolicy.EnforceLayerBoundaries);
        Assert.True(designPatternPolicy.EnforceCommandPattern);
        Assert.True(designPatternPolicy.EnforceStrategyPattern);
        Assert.True(designPatternPolicy.EnforceObserverPattern);
    }
    [Fact]
    public void AddAegisPolicies_loads_the_policy_directory_when_optional_documents_are_missing()
    {
        var configurationDirectory = Path.Combine(AppContext.BaseDirectory, "config");
        var architecturePath = Path.Combine(configurationDirectory, "architecture.policy.json");
        var directoryExisted = Directory.Exists(configurationDirectory);
        var previousArchitectureDocument = File.Exists(architecturePath)
            ? File.ReadAllText(architecturePath)
            : null;

        try
        {
            Directory.CreateDirectory(configurationDirectory);
            File.WriteAllText(
                architecturePath,
                """
                {
                  "EnforceLayerBoundaries": true,
                  "DesignPatterns": {
                    "EnforceCommandPattern": true
                  }
                }
                """);

            var services = new ServiceCollection();
            services.AddAegisPolicies();

            using var provider = services.BuildServiceProvider();
            var policy = provider
                .GetRequiredService<IOptions<AegisArchitecturePolicy>>()
                .Value;

            Assert.True(policy.Architecture.EnforceLayerBoundaries);
            Assert.True(policy.Architecture.DesignPatterns.EnforceCommandPattern);
        }
        finally
        {
            if (previousArchitectureDocument is null)
            {
                if (File.Exists(architecturePath))
                    File.Delete(architecturePath);
            }
            else
            {
                File.WriteAllText(architecturePath, previousArchitectureDocument);
            }

            if (!directoryExisted
                && Directory.Exists(configurationDirectory)
                && !Directory.EnumerateFileSystemEntries(configurationDirectory).Any())
            {
                Directory.Delete(configurationDirectory);
            }
        }
    }

}
