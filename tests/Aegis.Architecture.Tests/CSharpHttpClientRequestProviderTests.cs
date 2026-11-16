using Aegis.Architecture.Analysis.Api;
using Aegis.Shared.Architecture.Enums;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class CSharpHttpClientRequestProviderTests
{
    [Fact]
    public void DiscoversTypedHttpClientCallsAndPreservesDynamicUriExpressions()
    {
        using var fixture = new CSharpFixture(
            """
            using System.Net.Http;

            public sealed class OrdersClient
            {
                private readonly HttpClient _client;

                public OrdersClient(HttpClient client) => _client = client;

                public Task GetOrders() => _client.GetAsync("/orders");

                public Task GetOrder(string id) => _client.GetAsync($"/orders/{id}");
            }
            """);

        var evidence = new CSharpHttpClientRequestProvider().Discover(
            [fixture.SourcePath],
            fixture.ModulePath);

        Assert.Equal(2, evidence.Count);
        Assert.Contains(evidence, item =>
            item.HttpMethod == "GET"
            && item.RequestUriExpression == ""/orders""
            && item.ResolutionStatus == ApiClientRequestResolutionStatus.Resolved);
        Assert.Contains(evidence, item =>
            item.HttpMethod == "GET"
            && item.RequestUriExpression == "$"/orders/{id}""
            && item.ResolutionStatus == ApiClientRequestResolutionStatus.PartiallyResolved);
        Assert.All(evidence, item =>
        {
            Assert.Equal("C#", item.Language);
            Assert.Equal("System.Net.Http.HttpClient", item.Framework);
            Assert.Equal(fixture.ModulePath, item.ModulePath);
            Assert.Equal(fixture.SourcePath, item.SourceFile);
            Assert.NotEmpty(item.EvidenceId);
            Assert.NotEmpty(item.CallerIdentity);
            Assert.NotEmpty(item.DeclarationEvidence);
        });
    }

    [Fact]
    public void DoesNotTreatArbitraryGetMethodsAsHttpRequests()
    {
        using var fixture = new CSharpFixture(
            """
            public sealed class Cache
            {
                public object Get(string key) => new();
            }

            public sealed class Consumer
            {
                private readonly Cache _cache = new();

                public object Read() => _cache.Get("/not-an-http-request");
            }
            """);

        var evidence = new CSharpHttpClientRequestProvider().Discover(
            [fixture.SourcePath],
            fixture.ModulePath);

        Assert.Empty(evidence);
    }

    [Fact]
    public void DiscoversSendAsyncWhenHttpRequestMessageIsLocallyConstructed()
    {
        using var fixture = new CSharpFixture(
            """
            using System.Net.Http;

            public sealed class OrdersClient
            {
                private readonly HttpClient _client;

                public OrdersClient(HttpClient client) => _client = client;

                public Task Send()
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
                    return _client.SendAsync(request);
                }
            }
            """);

        var evidence = Assert.Single(new CSharpHttpClientRequestProvider().Discover(
            [fixture.SourcePath],
            fixture.ModulePath));

        Assert.Equal("POST", evidence.HttpMethod);
        Assert.Equal(""/orders"", evidence.RequestUriExpression);
        Assert.Contains("HttpRequestMessage", string.Join(" ", evidence.ResolutionEvidence));
    }

    private sealed class CSharpFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "aegis-csharp-http-client",
            Guid.NewGuid().ToString("N"));

        public CSharpFixture(string source)
        {
            Directory.CreateDirectory(_directory);
            ModulePath = _directory;
            SourcePath = Path.Combine(_directory, "OrdersClient.cs");
            File.WriteAllText(SourcePath, source);
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
