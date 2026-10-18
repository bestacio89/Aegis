using Aegis.Architecture.Bootstrap;
using Aegis.Architecture.RuleEngines;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureDependencyInjectionTests
{
    [Fact]
    public void AddAegisCore_registers_every_concrete_evaluator_in_the_architecture_assembly()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var returned = services.AddAegisCore();

        Assert.Same(services, returned);

        var expectedTypes = typeof(RuleEngine).Assembly
            .GetTypes()
            .Where(type =>
                type is { IsAbstract: false, IsInterface: false }
                && typeof(IEvaluator).IsAssignableFrom(type))
            .ToHashSet();

        var registeredTypes = services
            .Where(descriptor => descriptor.ServiceType == typeof(IEvaluator))
            .Select(descriptor => descriptor.ImplementationType)
            .Where(type => type is not null)
            .Cast<Type>()
            .ToHashSet();

        Assert.NotEmpty(expectedTypes);
        Assert.Subset(registeredTypes, expectedTypes);
        Assert.Subset(expectedTypes, registeredTypes);
    }

    [Fact]
    public void AddMissingArchitectureEvaluators_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAegisCore();

        var before = services
            .Where(descriptor => descriptor.ServiceType == typeof(IEvaluator))
            .Select(descriptor => (descriptor.ImplementationType, descriptor.Lifetime))
            .ToArray();

        services.AddMissingArchitectureEvaluators();

        var after = services
            .Where(descriptor => descriptor.ServiceType == typeof(IEvaluator))
            .Select(descriptor => (descriptor.ImplementationType, descriptor.Lifetime))
            .ToArray();

        Assert.Equal(before, after);
    }

    [Fact]
    public void Core_engines_are_registered_as_singletons()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAegisCore();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(RuleEngine)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(RuleEngineCore)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
    }
}
