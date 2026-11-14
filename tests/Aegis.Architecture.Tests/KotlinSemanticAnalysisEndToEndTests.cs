using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinSemanticAnalysisEndToEndTests
{
    [RequiresKotlinWorkerPackage]
    public async Task Analyses_kotlin_source_and_retains_host_owned_jvm_output()
    {
        var packageDirectory = Environment.GetEnvironmentVariable("AEGIS_JAVA_WORKER_PACKAGE")
            ?? throw new InvalidOperationException("The Kotlin worker package path was not configured.");
        var workspace = Directory.CreateTempSubdirectory("aegis-kotlin-e2e-");
        var sourceRoot = Path.Combine(workspace.FullName, "src", "main", "kotlin");
        var sourceFile = Path.Combine(sourceRoot, "sample", "WorkerProbe.kt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(
            sourceFile,
            """
            package sample

            class WorkerProbe {
                fun execute(): String = "worker-ready"
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<KotlinWorkerOptions>(_ => { });
        services.AddSingleton<IKotlinWorkerClient, KotlinWorkerClient>();
        await using var provider = services.BuildServiceProvider();
        var workerClient = provider.GetRequiredService<IKotlinWorkerClient>();
        var adapter = new KotlinSemanticAnalysisAdapter(
            workerClient,
            NullLogger<KotlinSemanticAnalysisAdapter>.Instance);
        await using var executionContext = new SemanticAnalysisExecutionContext(
            NullLogger<SemanticAnalysisExecutionContext>.Instance);

        try
        {
            Assert.True(Directory.Exists(packageDirectory));

            var request = new SemanticAnalysisRequest
            {
                Language = "Kotlin",
                ProjectPath = workspace.FullName,
                WorkspaceRoot = workspace.FullName,
                ModuleName = "worker-probe",
                BuildSystem = "Gradle",
                SourceFiles = [sourceFile],
                SourceRoots = [sourceRoot],
                RequestedCapabilities = ["symbols", "relationships", "compiler-diagnostics", "jvm-interop"],
                ProviderOptions = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["kotlin.compilerVersion"] = "2.4.21",
                    ["kotlin.languageVersion"] = "2.4",
                    ["kotlin.apiVersion"] = "2.4",
                    ["kotlin.jvmTarget"] = "21",
                    ["kotlin.moduleName"] = "worker_probe",
                    ["kotlin.configurationInferred"] = "false"
                }
            };

            var run = await adapter.AnalyzeAsync(request, executionContext);

            Assert.True(
                run.Status == SemanticAnalysisStatus.Completed,
                $"Expected completed Kotlin analysis, received {run.Status}: {string.Join(" | ", run.Diagnostics.Select(diagnostic => $"{diagnostic.Code} ({diagnostic.ExceptionType}): {diagnostic.Message}"))}");
            Assert.Contains(run.Symbols, symbol =>
                symbol.QualifiedName == "sample.WorkerProbe"
                && symbol.Language == "Kotlin");
            var outputDirectory = Assert.Single(
                executionContext.GetCompilationOutputs(workspace.FullName, includeTestCode: false));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "sample", "WorkerProbe.class")));
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }
}
