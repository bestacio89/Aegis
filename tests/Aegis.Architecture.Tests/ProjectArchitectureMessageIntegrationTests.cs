using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ProjectArchitectureMessageIntegrationTests
{
    [Fact]
    public void Detect_projects_module_owned_mass_transit_evidence_into_the_shared_registry()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "Orders.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="MassTransit" Version="8.3.0" /></ItemGroup>
            </Project>
            """);

        var sourceFile = Path.Combine(directory.Path, "OrderMessaging.cs");
        File.WriteAllText(
            sourceFile,
            """
            using System;
            using System.Threading.Tasks;
            using MassTransit;
            using Orders.Contracts;

            namespace MassTransit
            {
                public interface IConsumer<T> { }
                public interface IPublishEndpoint { Task Publish<T>(T message); }
                public interface IReceiveEndpointConfigurator { }
                public interface IBusFactoryConfigurator { }
                public interface ConsumeContext<T> { }

                public sealed class EndpointConfiguration : IBusFactoryConfigurator { }

                public static class RegistrationExtensions
                {
                    public static void ReceiveEndpoint(
                        this IBusFactoryConfigurator configurator,
                        string name,
                        Action<IReceiveEndpointConfigurator> configure) { }

                    public static void ConfigureConsumer<T>(
                        this IReceiveEndpointConfigurator configurator,
                        object context) { }
                }
            }
            using Orders.Contracts;
            namespace Orders.Contracts { public sealed record OrderSubmitted(Guid Id); }
            namespace Orders.Application
            {
                public sealed class OrderPublisher
                {
                    private readonly IPublishEndpoint _endpoint;
                    public OrderPublisher(IPublishEndpoint endpoint) => _endpoint = endpoint;
                    public Task Publish(OrderSubmitted message) => _endpoint.Publish(message);
                }
                public sealed class OrderConsumer : IConsumer<OrderSubmitted>
                {
                    public Task Consume(ConsumeContext<OrderSubmitted> context) => Task.CompletedTask;
                }

                public sealed class MessagingRegistration
                {
                    private readonly EndpointConfiguration _endpoints = new();

                    public void Configure() =>
                        _endpoints.ReceiveEndpoint(
                            "orders",
                            endpoint => endpoint.ConfigureConsumer<OrderConsumer>(this));
                }
            }
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        var module = Assert.Single(context.Modules);
        Assert.Contains("C#", module.Languages);
        var registry = Assert.IsType<IntegrationEvidenceRegistry<MessageIntegrationEvidence>>(
            context.MessageIntegrationEvidenceRegistry);
        Assert.Equal(2, registry.Count);
        Assert.All(registry.Evidence, evidence =>
        {
            Assert.Equal(directory.Path, evidence.ModulePath);
            Assert.Equal(sourceFile, evidence.SourceFile);
            Assert.Equal(IntegrationEvidenceResolutionStatus.Resolved, evidence.ResolutionStatus);
        });
        Assert.Contains(registry.Evidence, evidence =>
            evidence.RelationshipKind == ArchitectureRelationshipKind.MessagePublish);
        var consume = Assert.Single(registry.Evidence, evidence =>
            evidence.RelationshipKind == ArchitectureRelationshipKind.MessageConsume);
        Assert.Equal("orders", consume.DestinationIdentity);
        Assert.Equal(MessageDestinationKind.Unknown, consume.DestinationKind);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "aegis-message-integration",
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
