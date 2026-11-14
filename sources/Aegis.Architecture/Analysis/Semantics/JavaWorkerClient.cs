using System.Text;
using System.Text.Json;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Owns one bounded, long-lived Java worker process. Requests are serialized
/// over the JSONL channel; a timed-out or unhealthy process is terminated and
/// recreated before a subsequent request.
/// </summary>
public sealed class JavaWorkerClient : IJavaWorkerClient, IAsyncDisposable
{
    private const int ProtocolVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOptionsMonitor<JavaWorkerOptions> _options;
    private readonly ILogger<JavaWorkerClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JvmWorkerProcess _workerProcess;
    private bool _disposed;

    public JavaWorkerClient(
        IOptionsMonitor<JavaWorkerOptions> options,
        ILogger<JavaWorkerClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
        _workerProcess = new JvmWorkerProcess(logger);
    }

    public bool IsConfigured => _options.CurrentValue.IsConfigured;

    public async ValueTask<SemanticAnalysisRun> AnalyzeAsync(
        SemanticAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var options = _options.CurrentValue;
            if (!options.IsConfigured)
                throw new InvalidOperationException(
                    "The Eclipse JDT worker is not deployed. Configure JavaWorkerOptions and package the worker classes and runtime libraries.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.RequestTimeout);
            var launchOptions = CreateLaunchOptions(options);

            try
            {
                await _workerProcess.StartAsync(launchOptions, timeout.Token).ConfigureAwait(false);
                var startedAt = DateTimeOffset.UtcNow;
                var sourceManifestPath = Path.Combine(Path.GetTempPath(), $"aegis-java-sources-{Guid.NewGuid():N}.json");
                try
                {
                    var sourceManifest = JsonSerializer.Serialize(request.SourceFiles, JsonOptions);
                    await File.WriteAllTextAsync(sourceManifestPath, sourceManifest, Encoding.UTF8, timeout.Token)
                        .ConfigureAwait(false);

                    var payload = new
                    {
                        projectPath = request.ProjectPath,
                        workspaceRoot = request.WorkspaceRoot,
                        sourceRoots = request.SourceRoots,
                        referencedSourceRoots = request.ReferencedSourceRoots,
                        testSourceRoots = request.TestSourceRoots,
                        generatedSourceRoots = request.GeneratedSourceRoots,
                        sourceManifestPath,
                        classpathEntries = request.ClasspathEntries,
                        sourceLevel = request.SourceLevel ?? options.SourceLevel,
                        includeTestCode = request.IncludeTestCode,
                        includeGeneratedCode = request.IncludeGeneratedCode
                    };

                    var result = await _workerProcess.SendAsync(launchOptions, "analyze", payload, timeout.Token)
                        .ConfigureAwait(false);
                    return MapResult(result, request, startedAt);
                }
                finally
                {
                    try { File.Delete(sourceManifestPath); }
                    catch (IOException exception)
                    {
                        _logger.LogWarning(exception, "Could not remove temporary Java source manifest {ManifestPath}.", sourceManifestPath);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        _logger.LogWarning(exception, "Access to temporary Java source manifest {ManifestPath} was denied during cleanup.", sourceManifestPath);
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await _workerProcess.StopAsync().ConfigureAwait(false);
                throw new TimeoutException(
                    $"Eclipse JDT analysis exceeded the configured timeout of {options.RequestTimeout}.");
            }
            catch
            {
                await _workerProcess.StopAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static JvmWorkerLaunchOptions CreateLaunchOptions(JavaWorkerOptions options)
    {
        var classpath = string.Join(
            Path.PathSeparator,
            options.WorkerClassesDirectory,
            Path.Combine(options.WorkerLibrariesDirectory, "*"));

        return new JvmWorkerLaunchOptions(
            options.JavaExecutablePath,
            AppContext.BaseDirectory,
            ["-cp", classpath, "org.aegis.worker.WorkerMain"],
            ProtocolVersion,
            options.StartupTimeout,
            options.MaxFrameBytes,
            options.MaxResponseBytes,
            "Java semantic worker");
    }

    private static SemanticAnalysisRun MapResult(
        JsonElement result,
        SemanticAnalysisRequest request,
        DateTimeOffset startedAt)
    {
        return new SemanticAnalysisRun
        {
            RunId = Guid.NewGuid().ToString("N"),
            Language = "Java",
            EngineId = result.GetProperty("engineId").GetString() ?? "eclipse-jdt",
            EngineVersion = result.GetProperty("engineVersion").GetString(),
            ProjectPath = request.ProjectPath,
            Status = (SemanticAnalysisStatus)result.GetProperty("status").GetInt32(),
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            AnalyzedFileCount = result.GetProperty("analyzedFileCount").GetInt32(),
            SkippedFileCount = result.GetProperty("skippedFileCount").GetInt32(),
            Capabilities = ["symbols", "relationships", "compiler-diagnostics"],
            Symbols = DeserializeArray<SemanticSymbol>(result, "symbols"),
            Relationships = DeserializeArray<SemanticRelationship>(result, "relationships"),
            Diagnostics = DeserializeArray<AnalysisDiagnostic>(result, "diagnostics")
        };
    }

    private static List<T> DeserializeArray<T>(JsonElement result, string propertyName) =>
        result.TryGetProperty(propertyName, out var value)
            ? JsonSerializer.Deserialize<List<T>>(value.GetRawText(), JsonOptions) ?? []
            : [];

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await _workerProcess.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
