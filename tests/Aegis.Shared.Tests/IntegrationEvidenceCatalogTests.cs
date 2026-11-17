using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class IntegrationEvidenceCatalogTests
{
    [Fact]
    public void Catalog_unifies_language_specific_evidence_without_replacing_domain_contracts()
    {
        var javaPublish = new MessageIntegrationEvidence
        {
            EvidenceId = "duplicate-provider-id",
            Language = "Java",
            Framework = "Spring Kafka",
            ProviderId = "java-jdt",
            ModulePath = "/repo/orders",
            Operation = MessageIntegrationOperation.Publish,
            MessageTypeIdentity = "orders.OrderSubmitted",
            DestinationIdentity = "orders",
            ResolutionStatus = IntegrationEvidenceResolutionStatus.Resolved
        };
        var kotlinConsume = new MessageIntegrationEvidence
        {
            EvidenceId = "duplicate-provider-id",
            Language = "Kotlin",
            Framework = "Spring Kafka",
            ProviderId = "kotlin-k2-ir",
            ModulePath = "/repo/orders-consumer",
            Operation = MessageIntegrationOperation.Consume,
            MessageTypeExpression = "eventType",
            DestinationExpression = "${topics.orders}",
            ResolutionStatus = IntegrationEvidenceResolutionStatus.Dynamic
        };
        var javaRpc = new RpcIntegrationEvidence
        {
            EvidenceId = "rpc-call",
            Language = "Java",
            Framework = "gRPC",
            ProviderId = "java-jdt",
            ModulePath = "/repo/orders",
            Operation = RpcIntegrationOperation.Call,
            Protocol = "gRPC",
            ServiceIdentity = "orders.OrderService",
            MethodIdentity = "GetOrder",
            ResolutionStatus = IntegrationEvidenceResolutionStatus.PartiallyResolved
        };
        var csharpData = new DataAccessEvidence
        {
            EvidenceId = "data-read",
            Language = "C#",
            Framework = "Entity Framework Core",
            ProviderId = "roslyn-efcore",
            ModulePath = "/repo/orders",
            Operation = DataIntegrationOperation.Read,
            Technology = "Entity Framework Core",
            StoreIdentity = "OrdersDbContext",
            DataObjectIdentity = "Orders.Order",
            ResolutionStatus = IntegrationEvidenceResolutionStatus.Resolved
        };

        var catalog = new IntegrationEvidenceCatalog(
            new IntegrationEvidenceRegistry<MessageIntegrationEvidence>([javaPublish, kotlinConsume]),
            new IntegrationEvidenceRegistry<RpcIntegrationEvidence>([javaRpc]),
            new IntegrationEvidenceRegistry<DataAccessEvidence>([csharpData]));

        Assert.Equal(4, catalog.Count);
        Assert.Equal(
            new[]
            {
                IntegrationEvidenceDomain.Messaging,
                IntegrationEvidenceDomain.Messaging,
                IntegrationEvidenceDomain.Rpc,
                IntegrationEvidenceDomain.DataAccess
            },
            catalog.Evidence.Select(item => item.Domain));
        Assert.Equal(new[] { 0, 1 }, catalog.Evidence.Take(2).Select(item => item.OccurrenceIndex));
        Assert.Same(javaPublish, catalog.Evidence[0].Evidence);
        Assert.Same(kotlinConsume, catalog.Evidence[1].Evidence);
        Assert.Same(javaRpc, catalog.Evidence[2].Evidence);
        Assert.Same(csharpData, catalog.Evidence[3].Evidence);

        Assert.Equal(new IIntegrationEvidence[] { javaPublish, javaRpc }, catalog.ForLanguage("JAVA").Select(item => item.Evidence));
        Assert.Equal(new IIntegrationEvidence[] { javaPublish, javaRpc, csharpData }, catalog.ForModule("/REPO/ORDERS").Select(item => item.Evidence));
        Assert.Equal(new IIntegrationEvidence[] { javaPublish, kotlinConsume }, catalog.ForProvider("kotlin-k2-ir").Concat(catalog.ForProvider("java-jdt")).Where(item => item.Domain == IntegrationEvidenceDomain.Messaging).Select(item => item.Evidence).OrderBy(item => item.Language, StringComparer.Ordinal));
        Assert.Equal(new IIntegrationEvidence[] { javaPublish, csharpData }, catalog.ForStatus(IntegrationEvidenceResolutionStatus.Resolved).Select(item => item.Evidence));
        Assert.Single(catalog.ForRelationship(ArchitectureRelationshipKind.DataRead));
    }

    [Fact]
    public void Catalog_preserves_duplicate_ids_as_distinct_domain_occurrences()
    {
        var first = new MessageIntegrationEvidence
        {
            EvidenceId = "shared-id",
            Language = "Java",
            ProviderId = "java-jdt",
            Operation = MessageIntegrationOperation.Publish
        };
        var second = new RpcIntegrationEvidence
        {
            EvidenceId = "shared-id",
            Language = "Kotlin",
            ProviderId = "kotlin-k2-ir",
            Operation = RpcIntegrationOperation.Call
        };

        var catalog = new IntegrationEvidenceCatalog(
            new IntegrationEvidenceRegistry<MessageIntegrationEvidence>([first]),
            new IntegrationEvidenceRegistry<RpcIntegrationEvidence>([second]),
            null);

        Assert.Equal(2, catalog.Count);
        Assert.NotSame(catalog.Evidence[0], catalog.Evidence[1]);
        Assert.Equal(IntegrationEvidenceDomain.Messaging, catalog.Evidence[0].Domain);
        Assert.Equal(IntegrationEvidenceDomain.Rpc, catalog.Evidence[1].Domain);
        Assert.Same(first, catalog.Evidence[0].Evidence);
        Assert.Same(second, catalog.Evidence[1].Evidence);
    }

    [Fact]
    public void Catalog_supports_empty_domain_snapshots()
    {
        var catalog = new IntegrationEvidenceCatalog(null, null, null);

        Assert.Empty(catalog.Evidence);
        Assert.Empty(catalog.ForLanguage("C#"));
        Assert.Empty(catalog.ForModule("/repo"));
        Assert.Empty(catalog.ForProvider("provider"));
        Assert.Empty(catalog.ForRelationship(ArchitectureRelationshipKind.Unknown));
        Assert.Empty(catalog.ForStatus(IntegrationEvidenceResolutionStatus.Unresolved));
    }
}
