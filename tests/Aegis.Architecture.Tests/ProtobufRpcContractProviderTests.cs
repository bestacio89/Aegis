using Aegis.Architecture.Analysis;
using Aegis.Architecture.Analysis.Integrations.Rpc;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ProtobufRpcContractProviderTests
{
    [Fact]
    public void Discovers_unary_and_streaming_rpc_methods_with_package_qualified_identity()
    {
        using var directory = new TemporaryDirectory();
        var sourceFile = Path.Combine(directory.Path, "orders.proto");
        File.WriteAllText(sourceFile, """
            syntax = "proto3";
            package commerce.orders.v1;

            // Contract declarations are independent of client usage.
            service Orders {
                rpc GetOrder (GetOrderRequest) returns (GetOrderResponse);
                rpc WatchOrders (stream WatchOrdersRequest) returns (stream WatchOrdersResponse) {
                    option deprecated = false;
                }
            }
            """);

        var evidence = new ProtobufRpcContractProvider()
            .Discover([sourceFile], directory.Path);

        Assert.Equal(2, evidence.Count);
        Assert.All(evidence, item =>
        {
            Assert.Equal("Protocol Buffers", item.Language);
            Assert.Equal("Protocol Buffers gRPC", item.Framework);
            Assert.Equal("gRPC", item.Protocol);
            Assert.Equal("commerce.orders.v1.Orders", item.ServiceIdentity);
            Assert.Equal(RpcIntegrationOperation.Endpoint, item.Operation);
            Assert.Equal(IntegrationEvidenceResolutionStatus.Resolved, item.ResolutionStatus);
        });

        var unary = Assert.Single(evidence, item => item.MethodIdentity?.EndsWith(".GetOrder", StringComparison.Ordinal) == true);
        Assert.Equal("commerce.orders.v1.Orders.GetOrder", unary.TargetIdentity);
        Assert.Equal("GetOrderRequest -> GetOrderResponse", unary.TargetExpression);

        var streaming = Assert.Single(evidence, item => item.MethodIdentity?.EndsWith(".WatchOrders", StringComparison.Ordinal) == true);
        Assert.Equal("WatchOrdersRequest -> WatchOrdersResponse", streaming.TargetExpression);
        Assert.True(streaming.StartLine > 0);
        Assert.True(streaming.StartColumn > 0);
    }

    [Fact]
    public void Preserves_incomplete_rpc_declarations_as_partially_resolved_evidence()
    {
        using var directory = new TemporaryDirectory();
        var sourceFile = Path.Combine(directory.Path, "incomplete.proto");
        File.WriteAllText(sourceFile, """
            package commerce;
            service Orders {
                rpc MissingResponse (Request);
            }
            """);

        var evidence = Assert.Single(new ProtobufRpcContractProvider()
            .Discover([sourceFile], directory.Path));

        Assert.Equal("commerce.Orders.MissingResponse", evidence.MethodIdentity);
        Assert.Equal(IntegrationEvidenceResolutionStatus.PartiallyResolved, evidence.ResolutionStatus);
        Assert.Contains("incomplete", evidence.ResolutionEvidence.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_integrates_proto_contracts_into_the_shared_occurrence_preserving_registry()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "Orders.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(directory.Path, "orders.proto"),
            """
            syntax = "proto3";
            package commerce;
            service Orders {
                rpc GetOrder (Request) returns (Response);
            }
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        var registry = Assert.IsType<IntegrationEvidenceRegistry<RpcIntegrationEvidence>>(
            context.RpcIntegrationEvidenceRegistry);
        var endpoint = Assert.Single(registry.Evidence);
        Assert.Equal(directory.Path, endpoint.ModulePath);
        Assert.Equal("commerce.Orders.GetOrder", endpoint.MethodIdentity);
        Assert.Equal(ArchitectureRelationshipKind.RpcEndpoint, endpoint.RelationshipKind);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "aegis-protobuf-rpc",
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
