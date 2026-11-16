using System.Text.Json;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class IntegrationEvidenceRegistryTests
{
    [Fact]
    public void Registry_preserves_duplicate_ids_and_indexes_every_occurrence()
    {
        var first = CreateMessage("shared-id", "/repo/orders", IntegrationEvidenceResolutionStatus.Resolved);
        var second = CreateMessage("shared-id", "/repo/orders", IntegrationEvidenceResolutionStatus.Dynamic);
        var third = CreateMessage("other-id", "/repo/catalog", IntegrationEvidenceResolutionStatus.Unsupported);
        var registry = new IntegrationEvidenceRegistry<MessageIntegrationEvidence>([first, second, third]);

        Assert.Equal(3, registry.Count);
        Assert.Equal(new[] { first, second, third }, registry.Evidence);
        Assert.Equal(new[] { first, second }, registry.ForEvidenceId("shared-id"));
        Assert.Equal(new[] { first, second }, registry.ForModule("/REPO/ORDERS"));
        Assert.Equal(new[] { first }, registry.ForStatus(IntegrationEvidenceResolutionStatus.Resolved));
        Assert.Equal(new[] { second }, registry.ForStatus(IntegrationEvidenceResolutionStatus.Dynamic));
        Assert.Empty(registry.ForEvidenceId("missing-id"));
    }

    [Fact]
    public void Registry_rejects_null_evidence_occurrences()
    {
        var evidence = new MessageIntegrationEvidence[] { null! };

        Assert.Throws<ArgumentException>(() =>
            new IntegrationEvidenceRegistry<MessageIntegrationEvidence>(evidence));
    }

    [Fact]
    public void Domain_operations_map_to_the_existing_relationship_taxonomy()
    {
        var publish = CreateMessage("publish", "/repo/orders", IntegrationEvidenceResolutionStatus.Resolved);
        var consume = CreateMessage("consume", "/repo/orders", IntegrationEvidenceResolutionStatus.Resolved, MessageIntegrationOperation.Consume);
        var rpcCall = new RpcIntegrationEvidence { Operation = RpcIntegrationOperation.Call };
        var rpcEndpoint = new RpcIntegrationEvidence { Operation = RpcIntegrationOperation.Endpoint };
        var read = new DataAccessEvidence { Operation = DataIntegrationOperation.Read };
        var write = new DataAccessEvidence { Operation = DataIntegrationOperation.Write };
        var mapping = new DataAccessEvidence { Operation = DataIntegrationOperation.Mapping };

        Assert.Equal(ArchitectureRelationshipKind.MessagePublish, publish.RelationshipKind);
        Assert.Equal(ArchitectureRelationshipKind.MessageConsume, consume.RelationshipKind);
        Assert.Equal(ArchitectureRelationshipKind.RpcCall, rpcCall.RelationshipKind);
        Assert.Equal(ArchitectureRelationshipKind.RpcEndpoint, rpcEndpoint.RelationshipKind);
        Assert.Equal(ArchitectureRelationshipKind.DataRead, read.RelationshipKind);
        Assert.Equal(ArchitectureRelationshipKind.DataWrite, write.RelationshipKind);
        Assert.Equal(ArchitectureRelationshipKind.DataMapping, mapping.RelationshipKind);
    }

    [Fact]
    public void Derived_integration_registries_are_not_serialized_with_project_context()
    {
        var context = new ProjectArchitectureContext
        {
            MessageIntegrationEvidenceRegistry = new IntegrationEvidenceRegistry<MessageIntegrationEvidence>(
                [CreateMessage("message-id", "/repo/orders", IntegrationEvidenceResolutionStatus.Resolved)]),
            RpcIntegrationEvidenceRegistry = new IntegrationEvidenceRegistry<RpcIntegrationEvidence>(
                [new RpcIntegrationEvidence { EvidenceId = "rpc-id" }]),
            DataAccessEvidenceRegistry = new IntegrationEvidenceRegistry<DataAccessEvidence>(
                [new DataAccessEvidence { EvidenceId = "data-id" }])
        };

        var json = JsonSerializer.Serialize(context);

        Assert.DoesNotContain(nameof(ProjectArchitectureContext.MessageIntegrationEvidenceRegistry), json, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ProjectArchitectureContext.RpcIntegrationEvidenceRegistry), json, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ProjectArchitectureContext.DataAccessEvidenceRegistry), json, StringComparison.Ordinal);
    }

    private static MessageIntegrationEvidence CreateMessage(
        string evidenceId,
        string modulePath,
        IntegrationEvidenceResolutionStatus status,
        MessageIntegrationOperation operation = MessageIntegrationOperation.Publish) =>
        new()
        {
            EvidenceId = evidenceId,
            Language = "C#",
            Framework = "MassTransit",
            ProviderId = "roslyn-masstransit",
            ModulePath = modulePath,
            SourceFile = $"{modulePath}/Orders.cs",
            Operation = operation,
            MessageTypeIdentity = "Orders.Contracts.OrderSubmitted",
            ResolutionStatus = status,
            DeclarationEvidence = "Publish<OrderSubmitted>(message)"
        };
}
