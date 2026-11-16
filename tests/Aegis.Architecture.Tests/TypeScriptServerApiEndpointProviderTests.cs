using Aegis.Architecture.Analysis.Api;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class TypeScriptServerApiEndpointProviderTests
{
    [Fact]
    public void DiscoversExpressAndNestEndpointsFromCompilerBackedSyntax()
    {
        using var fixture = new TypeScriptFixture(
            """
            {
              "name": "orders-api",
              "private": true,
              "dependencies": { "express": "^5.0.0" }
            }
            """,
            """
            import express from "express";
            import { Controller, Get, Post } from "@nestjs/common";

            const app = express();
            const router = express.Router();

            app.get("/orders", listOrders);
            router.route("/accounts").get(listAccounts).post(createAccount);

            @Controller("api/orders")
            export class OrdersController {
                @Get(":id")
                getOrder() {}

                @Post()
                createOrder() {}
            }
            """);

        var diagnostics = new List<AnalysisDiagnostic>();
        var endpoints = new TypeScriptServerApiEndpointProvider().Discover(
            fixture.ModulePath,
            [fixture.SourcePath],
            diagnostics);

        Assert.True(
            diagnostics.Count == 0,
            string.Join(Environment.NewLine, diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        Assert.Equal(5, endpoints.Count);
        Assert.Contains(endpoints, endpoint =>
            endpoint.Framework == "Express"
            && endpoint.HttpMethods.SequenceEqual(["GET"])
            && endpoint.RouteTemplate == "/orders");
        Assert.Contains(endpoints, endpoint =>
            endpoint.Framework == "Express"
            && endpoint.HttpMethods.SequenceEqual(["GET"])
            && endpoint.RouteTemplate == "/accounts");
        Assert.Contains(endpoints, endpoint =>
            endpoint.Framework == "Express"
            && endpoint.HttpMethods.SequenceEqual(["POST"])
            && endpoint.RouteTemplate == "/accounts");
        Assert.Contains(endpoints, endpoint =>
            endpoint.Framework == "NestJS"
            && endpoint.HttpMethods.SequenceEqual(["GET"])
            && endpoint.RouteTemplate == "api/orders/:id");
        Assert.Contains(endpoints, endpoint =>
            endpoint.Framework == "NestJS"
            && endpoint.HttpMethods.SequenceEqual(["POST"])
            && endpoint.RouteTemplate == "api/orders");

        Assert.All(endpoints, endpoint =>
        {
            Assert.Equal("TypeScript", endpoint.Language);
            Assert.Equal(fixture.ModulePath, endpoint.ModulePath);
            Assert.Equal(fixture.SourcePath, endpoint.SourceFile);
            Assert.NotEmpty(endpoint.EvidenceId);
            Assert.NotEmpty(endpoint.DeclarationEvidence);
        });
    }

    [Fact]
    public void RetainsDynamicExpressRoutesAndDoesNotTreatUnrelatedGetCallsAsEndpoints()
    {
        using var fixture = new TypeScriptFixture(
            """
            {
              "name": "orders-api",
              "private": true,
              "dependencies": { "express": "^5.0.0" }
            }
            """,
            """
            import express from "express";

            const app = express();
            const route = configuration.ordersRoute;
            const cache = createCache();

            app.get(route, getOrders);
            cache.get("/not-an-endpoint", readCache);
            unknown.get("/also-not-an-endpoint", readUnknown);
            cache.route("/not-an-express-route").get(readCache);
            """);

        var endpoints = new TypeScriptServerApiEndpointProvider().Discover(
            fixture.ModulePath,
            [fixture.SourcePath]);

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("Dynamic", endpoint.ResolutionStatus.ToString());
        Assert.Equal("route", endpoint.RouteTemplate);
        Assert.Contains("app.get(route, getOrders)", endpoint.DeclarationEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoversExpressRouterEndpointsInJavaScriptModules()
    {
        using var fixture = new TypeScriptFixture(
            """
            {
              "name": "health-api",
              "private": true,
              "dependencies": { "express": "^5.0.0" }
            }
            """,
            """
            const express = require("express");
            const router = express.Router();
            router.post("/health/check", checkHealth);
            """,
            "routes.js");

        var endpoints = new TypeScriptServerApiEndpointProvider().Discover(
            fixture.ModulePath,
            [fixture.SourcePath]);

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("JavaScript", endpoint.Language);
        Assert.Equal("POST", Assert.Single(endpoint.HttpMethods));
        Assert.Equal("/health/check", endpoint.RouteTemplate);
    }

    private sealed class TypeScriptFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "aegis-typescript-api-endpoints",
            Guid.NewGuid().ToString("N"));

        public TypeScriptFixture(string packageJson, string source, string fileName = "routes.ts")
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
