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
        Assert.Contains(registry.Evidence, evidence =>
            evidence.RelationshipKind == ArchitectureRelationshipKind.MessageConsume);
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
