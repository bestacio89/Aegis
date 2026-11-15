using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinWorkerConfigurationTests
{
    [Theory]
    [InlineData("0m")]
    [InlineData("-1g")]
    [InlineData("1024")]
    [InlineData("1mgarbage")]
    [InlineData("1t")]
    public async Task Rejects_invalid_heap_sizes_before_starting_worker(string heapSize)
    {
        using var package = CreatePackage();
        var options = package.Options;
        options.MaxHeapSize = heapSize;
        await using var client = CreateClient(options);
        var artifactRoot = Directory.CreateTempSubdirectory("aegis-kotlin-config-");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.AnalyzeAsync(
                    CreateRequest(),
                    CreateCompilerConfiguration(),
                    artifactRoot.FullName).AsTask());
        }
        finally
        {
            artifactRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_non_positive_request_timeout_before_starting_worker()
    {
        using var package = CreatePackage();
        package.Options.RequestTimeout = TimeSpan.Zero;
        await using var client = CreateClient(package.Options);
        var artifactRoot = Directory.CreateTempSubdirectory("aegis-kotlin-config-");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.AnalyzeAsync(
                    CreateRequest(),
                    CreateCompilerConfiguration(),
                    artifactRoot.FullName).AsTask());
        }
        finally
        {
            artifactRoot.Delete(recursive: true);
        }
    }

    private static KotlinWorkerClient CreateClient(KotlinWorkerOptions options) =>
        new(
            new FixedOptionsMonitor<KotlinWorkerOptions>(options),
            NullLogger<KotlinWorkerClient>.Instance);

    private static SemanticAnalysisRequest CreateRequest() =>
        new()
        {
            Language = "Kotlin",
            ProjectPath = Path.GetTempPath(),
            WorkspaceRoot = Path.GetTempPath(),
            SourceFiles = ["Service.kt"],
            SourceRoots = ["src/main/kotlin"]
        };

    private static KotlinCompilerConfiguration CreateCompilerConfiguration() =>
        new("2.4.21", "2.4", "2.4", "21", "aegis_test");

    private static TemporaryWorkerPackage CreatePackage()
    {
        var root = Directory.CreateTempSubdirectory("aegis-kotlin-worker-package-");
        var libraries = Directory.CreateDirectory(Path.Combine(root.FullName, "lib"));
        var workerJar = Path.Combine(root.FullName, "kotlin-worker.jar");
        var standardLibrary = Path.Combine(libraries.FullName, "kotlin-stdlib-2.4.21.jar");
        File.WriteAllText(workerJar, string.Empty);
        File.WriteAllText(standardLibrary, string.Empty);
        File.WriteAllText(Path.Combine(libraries.FullName, "kotlin-compiler-embeddable-2.4.21.jar"), string.Empty);
        File.WriteAllText(Path.Combine(libraries.FullName, "gson-2.13.2.jar"), string.Empty);

        return new TemporaryWorkerPackage(
            root,
            new KotlinWorkerOptions
            {
                JavaExecutablePath = Path.Combine(root.FullName, "java-not-installed"),
                WorkerJarPath = workerJar,
                WorkerLibrariesDirectory = libraries.FullName,
                KotlinStandardLibraryPath = standardLibrary,
                MaxHeapSize = "1024m",
                RequestTimeout = TimeSpan.FromMinutes(2)
            });
    }

    private sealed class TemporaryWorkerPackage(DirectoryInfo root, KotlinWorkerOptions options) : IDisposable
    {
        public KotlinWorkerOptions Options { get; } = options;

        public void Dispose() => root.Delete(recursive: true);
    }

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
