using Aegis.Architecture.Analysis.Integrations.Messaging;
using Aegis.Shared.Architecture.Enums;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class CSharpMassTransitMessageProviderTests
{
    [Fact]
    public void Discovers_grounded_mass_transit_publish_and_consumer_contracts()
    {
        using var fixture = new CSharpMessagingFixture(
            """
            using System;
            using System.Threading.Tasks;
            using MassTransit;
            using Orders.Contracts;

            namespace Orders.Contracts
            {
                public sealed record OrderSubmitted(Guid Id);
            }

            namespace Orders.Application
            {
                public sealed class OrderPublisher
                {
                    private readonly IPublishEndpoint _publishEndpoint;

                    public OrderPublisher(IPublishEndpoint publishEndpoint)
                    {
                        _publishEndpoint = publishEndpoint;
                    }

                    public Task Publish(OrderSubmitted message) =>
                        _publishEndpoint.Publish(message);
                }

                public sealed class OrderSubmittedConsumer : IConsumer<OrderSubmitted>
                {
                    public Task Consume(ConsumeContext<OrderSubmitted> context) =>
                        Task.CompletedTask;
                }

                public sealed class MessagingRegistration
                {
                    private readonly EndpointConfiguration _endpoints = new();

                    public void Configure() =>
                        _endpoints.ReceiveEndpoint(
                            "orders",
                            endpoint => endpoint.ConfigureConsumer<OrderSubmittedConsumer>(this));
                }
            }
            """);

        var evidence = new CSharpMassTransitMessageProvider().Discover(
            [fixture.SourcePath],
            fixture.ModulePath);

        Assert.Equal(2, evidence.Count);
        var publish = Assert.Single(evidence, item => item.Operation == MessageIntegrationOperation.Publish);
        var consume = Assert.Single(evidence, item => item.Operation == MessageIntegrationOperation.Consume);

        Assert.Equal("C#", publish.Language);
        Assert.Equal("MassTransit", publish.Framework);
        Assert.Equal("roslyn-masstransit", publish.ProviderId);
        Assert.Equal(fixture.ModulePath, publish.ModulePath);
        Assert.Equal(fixture.SourcePath, publish.SourceFile);
        Assert.EndsWith("Orders.Contracts.OrderSubmitted", publish.MessageTypeIdentity, StringComparison.Ordinal);
        Assert.Equal("OrderSubmitted", publish.MessageTypeExpression);
        Assert.Equal(IntegrationEvidenceResolutionStatus.Resolved, publish.ResolutionStatus);
        Assert.Equal(MessageDestinationKind.Unknown, publish.DestinationKind);
        Assert.Contains("IPublishEndpoint", string.Join(" ", publish.ResolutionEvidence), StringComparison.Ordinal);

        Assert.EndsWith("Orders.Contracts.OrderSubmitted", consume.MessageTypeIdentity, StringComparison.Ordinal);
        Assert.Equal("OrderSubmitted", consume.MessageTypeExpression);
        Assert.Equal(IntegrationEvidenceResolutionStatus.Resolved, consume.ResolutionStatus);
        Assert.Contains("OrderSubmittedConsumer", consume.HandlerIdentity, StringComparison.Ordinal);
        Assert.Contains("IConsumer<OrderSubmitted>", consume.ResolutionEvidence[0], StringComparison.Ordinal);
        Assert.Equal("orders", consume.DestinationIdentity);
        Assert.Equal("\"orders\"", consume.DestinationExpression);
        Assert.Equal(MessageDestinationKind.Unknown, consume.DestinationKind);
        Assert.Contains("ReceiveEndpoint", consume.DeclarationEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserves_dynamic_receive_endpoint_expressions_without_inventing_destination_identity()
    {
        using var fixture = new CSharpMessagingFixture(
            """
            using System;
            using MassTransit;
            using Orders.Contracts;

            namespace Orders.Contracts
            {
                public sealed record OrderSubmitted;
            }

            namespace Orders.Application
            {
                public sealed class OrderConsumer : IConsumer<OrderSubmitted>
                {
                    public void Consume() { }
                }

                public sealed class MessagingRegistration
                {
                    private readonly EndpointConfiguration _endpoints = new();

                    public void Configure(string endpointName) =>
                        _endpoints.ReceiveEndpoint(
                            endpointName,
                            endpoint => endpoint.ConfigureConsumer<OrderConsumer>(this));
                }
            }
            """);

        var evidence = new CSharpMassTransitMessageProvider().Discover(
            [fixture.SourcePath],
            fixture.ModulePath);

        var consume = Assert.Single(evidence);
        Assert.Equal(MessageIntegrationOperation.Consume, consume.Operation);
        Assert.Null(consume.DestinationIdentity);
        Assert.Equal("endpointName", consume.DestinationExpression);
        Assert.Equal(MessageDestinationKind.Unknown, consume.DestinationKind);
        Assert.Contains("dynamic", string.Join(" ", consume.ResolutionEvidence), StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_treat_arbitrary_publish_methods_as_mass_transit_messages()
    {
        using var fixture = new CSharpMessagingFixture(
            """
            using MassTransit;

            namespace Orders.Application
            {
                public sealed record OrderSubmitted;
                public sealed class CustomPublisher
                {
                    public void Publish(OrderSubmitted message) { }
                }

                public sealed class OrderService
                {
                    private readonly CustomPublisher _publisher = new();

                    public void Submit(OrderSubmitted message) =>
                        _publisher.Publish(message);
                }
            }
            """);

        var evidence = new CSharpMassTransitMessageProvider().Discover(
            [fixture.SourcePath],
            fixture.ModulePath);

        Assert.Empty(evidence);
    }


    [Fact]
    public void Ignores_unrelated_consumer_and_endpoint_methods_with_mass_transit_like_names()
    {
        using var fixture = new CSharpMessagingFixture(
            """
            using System;
            using FakeMessaging;
            using MassTransit;

            namespace FakeMessaging
            {
                public interface IConsumer<T> { }
            }

            namespace Orders.Application
            {
                public sealed record OrderSubmitted;
                public sealed class OrderConsumer : IConsumer<OrderSubmitted> { }

                public sealed class EndpointConfiguration
                {
                    public void ReceiveEndpoint(string name, Action<EndpointConfiguration> configure) =>
                        configure(this);

                    public void ConfigureConsumer<T>(object context) { }
                }

                public sealed class Registration
                {
                    private readonly EndpointConfiguration _endpoints = new();

                    public void Configure() =>
                        _endpoints.ReceiveEndpoint(
                            "orders",
                            endpoint => endpoint.ConfigureConsumer<OrderConsumer>(this));
                }
            }
            """);

        var evidence = new CSharpMassTransitMessageProvider().Discover(
            [fixture.SourcePath],
            fixture.ModulePath);

        Assert.Empty(evidence);
    }

    private sealed class CSharpMessagingFixture : IDisposable
    {
        private const string MassTransitStubs = """
            namespace MassTransit
            {
                using System;
                using System.Threading.Tasks;

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
            """;
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "aegis-csharp-messaging",
            Guid.NewGuid().ToString("N"));

        public CSharpMessagingFixture(string source)
        {
            Directory.CreateDirectory(_directory);
            ModulePath = _directory;
            SourcePath = Path.Combine(_directory, "OrderMessaging.cs");
            var namespaceStart = source.IndexOf("namespace ", StringComparison.Ordinal);
            var sourceWithStubs = namespaceStart < 0
                ? source
                : source.Insert(namespaceStart, MassTransitStubs + Environment.NewLine);
            File.WriteAllText(SourcePath, sourceWithStubs);
        }

        public string ModulePath { get; }
        public string SourcePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
