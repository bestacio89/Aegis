using Aegis.Architecture.Analysis;
using Aegis.Architecture.Analysis.Integrations.Rpc;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ProjectArchitectureRpcIntegrationTests
{
    [Fact]
    public void Detect_combines_protobuf_contracts_and_grounded_grpc_client_calls()
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
                rpc GetOrder (GetOrderRequest) returns (GetOrderResponse);
            }
            """);
        File.WriteAllText(
            Path.Combine(directory.Path, "OrderClient.cs"),
            """
            using System.Threading.Tasks;

            namespace Grpc.Core
            {
                public abstract class ClientBase<T>
                {
                    public Task<TResult> WithCallCredentials<TResult>() => Task.FromResult(default(TResult)!);
                }
            }

            namespace Commerce
            {
                public sealed class GetOrderRequest { }
                public sealed class GetOrderResponse { }

                public static class Orders
                {
                    public sealed class OrdersClient : Grpc.Core.ClientBase<OrdersClient>
                    {
                        public Task<GetOrderResponse> GetOrderAsync(GetOrderRequest request) =>
                            Task.FromResult(new GetOrderResponse());
                    }
                }

                public sealed class OrderQuery
                {
                    private readonly Orders.OrdersClient _client;
                    public OrderQuery(Orders.OrdersClient client) => _client = client;

                    public Task<GetOrderResponse> ExecuteAsync(GetOrderRequest request) =>
                        _client.GetOrderAsync(request);

                    public Task<GetOrderResponse> ConfigureAsync() =>
                        _client.WithCallCredentials<GetOrderResponse>();
                }
            }

            namespace Unrelated
            {
                public sealed class OrdersClient
                {
                    public void GetOrderAsync() { }
                }
            }
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        var registry = Assert.IsType<IntegrationEvidenceRegistry<RpcIntegrationEvidence>>(
            context.RpcIntegrationEvidenceRegistry);
        Assert.Equal(2, registry.Count);

        var endpoint = Assert.Single(registry.Evidence, item =>
            item.Operation == RpcIntegrationOperation.Endpoint);
        Assert.Equal("commerce.Orders.GetOrder", endpoint.MethodIdentity);
        Assert.Equal(ArchitectureRelationshipKind.RpcEndpoint, endpoint.RelationshipKind);

        var call = Assert.Single(registry.Evidence, item =>
            item.Operation == RpcIntegrationOperation.Call);
        Assert.Equal("commerce.Orders.GetOrder", call.MethodIdentity);
        Assert.Equal("commerce.Orders", endpoint.ServiceIdentity);
        Assert.Equal("commerce.Orders.GetOrder", endpoint.TargetIdentity);
        Assert.Equal("_client", call.TargetExpression);
        Assert.Equal("OrderQuery.ExecuteAsync", call.HandlerIdentity);
        Assert.Equal(IntegrationEvidenceResolutionStatus.Resolved, call.ResolutionStatus);
        Assert.Equal(ArchitectureRelationshipKind.RpcCall, call.RelationshipKind);
    }

    [Fact]
    public void Ignores_unrelated_methods_that_only_share_a_grpc_client_name()
    {
        using var directory = new TemporaryDirectory();
        var sourceFile = Path.Combine(directory.Path, "Unrelated.cs");
        File.WriteAllText(sourceFile, """
            namespace Unrelated
            {
                public sealed class OrdersClient
                {
                    public void GetOrderAsync() { }
                }
            }
            """);

        var evidence = new CSharpGrpcClientCallProvider()
            .Discover([sourceFile], directory.Path);

        Assert.Empty(evidence);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "aegis-grpc-rpc",
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
