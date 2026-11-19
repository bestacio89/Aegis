using Aegis.Architecture.Analysis.Api;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class TypeScriptApiClientRequestProviderTests
{
    [Fact]
    public void DiscoversFetchAxiosAngularAndNestRequestsWithoutMatchingUnrelatedMethods()
    {
        using var fixture = new TypeScriptClientFixture(
            """
            {
              "name": "web-client",
              "private": true,
              "dependencies": {
                "axios": "^1.8.0",
                "@angular/common": "^21.0.0",
                "@nestjs/axios": "^4.0.0"
              }
            }
            """,
            """
            import axios from "axios";
            import { HttpClient as ApiHttpClient } from "@angular/common/http";
            import { HttpService } from "@nestjs/axios";

            class OrderGateway {
                constructor(
                    private readonly http: ApiHttpClient,
                    private readonly service: HttpService
                ) {}

                async load(orderId: string) {
                    this.http.get("/orders");
                    this.http.delete("/orders/42");
                    this.service.post("/orders", {});
                    axios.get("/catalog");
                    const client = axios.create({ baseURL: "/api" });
                    client.delete("/catalog/42");
                    fetch("/health");
                    fetch("/submit", { method: "POST" });
                    arbitrary.get("/not-an-api-client");
                }
            }
            """);

        var diagnostics = new List<AnalysisDiagnostic>();
        var evidence = new TypeScriptApiClientRequestProvider().Discover(
            fixture.ModulePath,
            [fixture.SourcePath],
            diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(7, evidence.Count);

        Assert.Contains(evidence, item =>
            item.Framework == "Angular HttpClient"
            && item.HttpMethod == "GET"
            && item.RequestUriExpression == "\"/orders\"");
        Assert.Contains(evidence, item =>
            item.Framework == "Angular HttpClient"
            && item.HttpMethod == "DELETE"
            && item.RequestUriExpression == "\"/orders/42\"");
        Assert.Contains(evidence, item =>
            item.Framework == "NestJS HttpService"
            && item.HttpMethod == "POST"
            && item.RequestUriExpression == "\"/orders\"");
        Assert.Contains(evidence, item =>
            item.Framework == "Axios"
            && item.HttpMethod == "GET"
            && item.RequestUriExpression == "\"/catalog\"");
        Assert.Contains(evidence, item =>
            item.Framework == "Axios"
            && item.HttpMethod == "DELETE"
            && item.RequestUriExpression == "\"/catalog/42\"");
        var axiosInstanceRequest = Assert.Single(evidence, item =>
            item.Framework == "Axios" && item.HttpMethod == "DELETE");
        Assert.Equal("\"/api\"", axiosInstanceRequest.BaseAddressExpression);
        Assert.Contains(evidence, item =>
            item.Framework == "Fetch API"
            && item.HttpMethod == "GET"
            && item.RequestUriExpression == "\"/health\"");
        Assert.Contains(evidence, item =>
            item.Framework == "Fetch API"
            && item.HttpMethod == "POST"
            && item.RequestUriExpression == "\"/submit\"");

        Assert.All(evidence, item =>
        {
            Assert.Equal("TypeScript", item.Language);
            Assert.Equal(fixture.ModulePath, item.ModulePath);
            Assert.Equal(fixture.SourcePath, item.SourceFile);
            Assert.NotEmpty(item.EvidenceId);
            Assert.NotEmpty(item.CallerIdentity);
            Assert.NotEmpty(item.DeclarationEvidence);
            Assert.NotEmpty(item.ResolutionEvidence);
        });
    }

    [Fact]
    public void RetainsDynamicRequestExpressionsAndAxiosRequestConfiguration()
    {
        using var fixture = new TypeScriptClientFixture(
            """
            {
              "name": "client",
              "private": true,
              "dependencies": { "axios": "^1.8.0" }
            }
            """,
            """
            import axios from "axios";

            async function send(orderId: string, endpoint: string) {
                fetch(`/orders/${orderId}`);
                axios.request({ url: endpoint, method: "PUT" });
                axios.request({ url: endpoint, method });
                unknown.get("/not-a-client");
            }
            """);

        var evidence = new TypeScriptApiClientRequestProvider().Discover(
            fixture.ModulePath,
            [fixture.SourcePath]);

        Assert.Equal(3, evidence.Count);
        var fetch = Assert.Single(evidence, item => item.Framework == "Fetch API");
        Assert.Equal("GET", fetch.HttpMethod);
        Assert.Equal(ApiClientRequestResolutionStatus.PartiallyResolved, fetch.ResolutionStatus);
        Assert.Contains("orderId", fetch.RequestUriExpression, StringComparison.Ordinal);

        var axios = Assert.Single(evidence, item => item.Framework == "Axios" && item.HttpMethod == "PUT");
        Assert.Equal("PUT", axios.HttpMethod);
        Assert.Equal(ApiClientRequestResolutionStatus.Dynamic, axios.ResolutionStatus);
        Assert.Equal("endpoint", axios.RequestUriExpression);
        var dynamicMethod = Assert.Single(evidence, item =>
            item.Framework == "Axios" && item.HttpMethod is null);
        Assert.Equal(ApiClientRequestResolutionStatus.Dynamic, dynamicMethod.ResolutionStatus);
    }

    [Fact]
    public void DiscoversCommonJsAxiosAndJavaScriptFetchCalls()
    {
        using var fixture = new TypeScriptClientFixture(
            """
            {
              "name": "legacy-client",
              "private": true,
              "dependencies": { "axios": "^1.8.0" }
            }
            """,
            """
            const axios = require("axios");
            const client = axios.create();

            async function load() {
                client.get("/legacy");
                globalThis.fetch("/status");
            }
            """,
            "client.js");

        var evidence = new TypeScriptApiClientRequestProvider().Discover(
            fixture.ModulePath,
            [fixture.SourcePath]);

        Assert.Equal(2, evidence.Count);
        Assert.All(evidence, item => Assert.Equal("JavaScript", item.Language));
        Assert.Contains(evidence, item => item.Framework == "Axios" && item.HttpMethod == "GET");
        Assert.Contains(evidence, item => item.Framework == "Fetch API" && item.HttpMethod == "GET");
    }

    private sealed class TypeScriptClientFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "aegis-typescript-api-clients",
            Guid.NewGuid().ToString("N"));

        public TypeScriptClientFixture(string packageJson, string source, string fileName = "client.ts")
        {
            Directory.CreateDirectory(_directory);
            ModulePath = _directory;
            SourcePath = Path.Combine(_directory, fileName);
            File.WriteAllText(Path.Combine(_directory, "package.json"), packageJson);
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
