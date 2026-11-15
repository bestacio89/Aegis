using Aegis.Architecture.Analysis.Semantics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class SemanticAnalysisExecutionContextTests
{
    [Fact]
    public async Task Registers_scope_specific_outputs_and_cleans_host_owned_artifacts()
    {
        var context = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);
        var modulePath = Path.Combine(Path.GetTempPath(), "aegis-module");
        var productionRoot = context.CreateArtifactRoot();
        var productionOutput = Path.Combine(productionRoot, "classes");
        Directory.CreateDirectory(productionOutput);
        context.RegisterCompilationOutput(modulePath, false, productionRoot, productionOutput);

        var testRoot = context.CreateArtifactRoot();
        var testOutput = Path.Combine(testRoot, "classes");
        Directory.CreateDirectory(testOutput);
        context.RegisterCompilationOutput(modulePath, true, testRoot, testOutput);

        Assert.Equal([productionOutput], context.GetCompilationOutputs(modulePath, false));
        Assert.Equal(
            new[] { productionOutput, testOutput },
            context.GetCompilationOutputs(modulePath, true));

        await context.DisposeAsync();

        Assert.False(Directory.Exists(productionRoot));
        Assert.False(Directory.Exists(testRoot));
    }

    [Fact]
    public async Task Resolves_dependency_outputs_without_leaking_production_dependencies_test_outputs()
    {
        await using var context = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);
        var consumerPath = Path.Combine(Path.GetTempPath(), "aegis-consumer");
        var productionDependencyPath = Path.Combine(Path.GetTempPath(), "aegis-production-dependency");
        var testDependencyPath = Path.Combine(Path.GetTempPath(), "aegis-test-dependency");

        var consumerProduction = RegisterOutput(context, consumerPath, isTestCode: false);
        var consumerTest = RegisterOutput(context, consumerPath, isTestCode: true);
        var productionDependency = RegisterOutput(context, productionDependencyPath, isTestCode: false);
        var productionDependencyTest = RegisterOutput(context, productionDependencyPath, isTestCode: true);
        var testDependencyProduction = RegisterOutput(context, testDependencyPath, isTestCode: false);
        var testDependencyTest = RegisterOutput(context, testDependencyPath, isTestCode: true);

        var outputs = context.GetCompilationOutputs(
            consumerPath,
            [productionDependencyPath, testDependencyPath],
            [testDependencyPath],
            includeTestCode: true);

        Assert.Contains(consumerProduction, outputs);
        Assert.Contains(consumerTest, outputs);
        Assert.Contains(productionDependency, outputs);
        Assert.Contains(testDependencyProduction, outputs);
        Assert.Contains(testDependencyTest, outputs);
        Assert.DoesNotContain(productionDependencyTest, outputs);
    }

    private static string RegisterOutput(
        SemanticAnalysisExecutionContext context,
        string modulePath,
        bool isTestCode,
        string? variantName = null)
    {
        var artifactRoot = context.CreateArtifactRoot();
        var output = Path.Combine(artifactRoot, "classes");
        Directory.CreateDirectory(output);
        context.RegisterCompilationOutput(modulePath, isTestCode, artifactRoot, output, variantName);
        return output;
    }

    [Fact]
    public async Task Keeps_compilation_outputs_isolated_by_exact_android_variant()
    {
        await using var context = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);
        var modulePath = Path.Combine(Path.GetTempPath(), "aegis-android-module");
        var debugOutput = RegisterOutput(context, modulePath, isTestCode: false, variantName: "debug");
        var releaseOutput = RegisterOutput(context, modulePath, isTestCode: false, variantName: "release");

        Assert.Equal([debugOutput], context.GetCompilationOutputs(modulePath, false, "debug"));
        Assert.Equal([releaseOutput], context.GetCompilationOutputs(modulePath, false, "release"));
        Assert.Empty(context.GetCompilationOutputs(modulePath, false));
        Assert.Empty(context.GetCompilationOutputs(modulePath, false, "staging"));
    }

    [Fact]
    public async Task Assembles_project_outputs_in_dependency_order_without_cross_variant_or_scope_leakage()
    {
        await using var context = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);
        var consumerPath = Path.Combine(Path.GetTempPath(), "aegis-android-consumer");
        var dependencyPath = Path.Combine(Path.GetTempPath(), "aegis-android-dependency");
        var consumerDebug = RegisterOutput(context, consumerPath, false, "debug");
        var dependencyDebug = RegisterOutput(context, dependencyPath, false, "debug");
        _ = RegisterOutput(context, dependencyPath, false, "release");
        var dependencyDebugTest = RegisterOutput(context, dependencyPath, true, "debug");

        var outputs = context.GetCompilationOutputs(
            consumerPath,
            [dependencyPath, dependencyPath],
            [dependencyPath],
            includeTestCode: true,
            variantName: "debug");

        Assert.Equal([consumerDebug, dependencyDebug, dependencyDebugTest], outputs);
    }

    [Fact]
    public async Task Rejects_compilation_outputs_outside_owned_artifact_root()
    {
        var context = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);
        var root = context.CreateArtifactRoot();
        var outside = Directory.CreateTempSubdirectory("aegis-unowned-").FullName;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
                context.RegisterCompilationOutput(
                    Path.Combine(Path.GetTempPath(), "aegis-module"),
                    false,
                    root,
                    outside)));
        }
        finally
        {
            await context.DisposeAsync();
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }
}
