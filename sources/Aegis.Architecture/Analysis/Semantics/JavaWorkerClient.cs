using System.Diagnostics;
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
    private Process? _process;
    private bool _disposed;

    public JavaWorkerClient(
        IOptionsMonitor<JavaWorkerOptions> options,
        ILogger<JavaWorkerClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
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

            try
            {
                await EnsureStartedAsync(options, timeout.Token).ConfigureAwait(false);
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
                        sourceRoots = request.SourceRoots,
                        testSourceRoots = request.TestSourceRoots,
                        generatedSourceRoots = request.GeneratedSourceRoots,
                        sourceManifestPath,
                        classpathEntries = request.ClasspathEntries,
                        sourceLevel = request.SourceLevel ?? options.SourceLevel,
                        includeTestCode = request.IncludeTestCode,
                        includeGeneratedCode = request.IncludeGeneratedCode
                    };

                    var result = await SendRequestAsync("analyze", payload, options.MaxFrameBytes, options.MaxResponseBytes, timeout.Token)
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
                await StopWorkerAsync().ConfigureAwait(false);
                throw new TimeoutException(
                    $"Eclipse JDT analysis exceeded the configured timeout of {options.RequestTimeout}.");
            }
            catch
            {
                await StopWorkerAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStartedAsync(
        JavaWorkerOptions options,
        CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
            return;

        await StopWorkerAsync().ConfigureAwait(false);

        var classpath = string.Join(
            Path.PathSeparator,
            options.WorkerClassesDirectory,
            Path.Combine(options.WorkerLibrariesDirectory, "*"));

        var startInfo = new ProcessStartInfo
        {
            FileName = options.JavaExecutablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(classpath);
        startInfo.ArgumentList.Add("org.aegis.worker.WorkerMain");

        var process = new Process { StartInfo = startInfo };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                _logger.LogWarning("Java semantic worker stderr: {WorkerMessage}", eventArgs.Data);
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The Eclipse JDT worker process could not be started.");
        }

        process.BeginErrorReadLine();
        _process = process;

        using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupTimeout.CancelAfter(options.StartupTimeout);
        var handshake = await SendRequestAsync(
            "handshake",
            null,
            options.MaxFrameBytes,
            options.MaxResponseBytes,
            startupTimeout.Token).ConfigureAwait(false);

        if (!handshake.TryGetProperty("protocolVersion", out var version)
            || version.GetInt32() != ProtocolVersion)
        {
            throw new InvalidDataException("The Java worker returned an incompatible protocol handshake.");
        }

        _logger.LogInformation(
            "Started Eclipse JDT semantic worker using protocol {ProtocolVersion}.",
            ProtocolVersion);
    }

    private async Task<JsonElement> SendRequestAsync(
        string operation,
        object? payload,
        int maxFrameBytes,
        int maxResponseBytes,
        CancellationToken cancellationToken)
    {
        var process = _process
            ?? throw new InvalidOperationException("The Java worker process is not running.");

        var requestId = Guid.NewGuid().ToString("N");
        var frame = JsonSerializer.Serialize(
            new
            {
                protocolVersion = ProtocolVersion,
                requestId,
                operation,
                payload
            },
            JsonOptions);

        if (Encoding.UTF8.GetByteCount(frame) > maxFrameBytes)
            throw new InvalidDataException(
                $"The Java worker request exceeds the configured {maxFrameBytes}-byte frame limit.");

        await process.StandardInput.WriteLineAsync(frame.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

        var responseLine = await process.StandardOutput.ReadLineAsync(cancellationToken)
            .ConfigureAwait(false);
        if (responseLine is null)
            throw new EndOfStreamException("The Java worker closed stdout before returning a response.");

        if (Encoding.UTF8.GetByteCount(responseLine) > maxResponseBytes)
            throw new InvalidDataException(
                $"The Java worker response exceeds the configured {maxResponseBytes}-byte response limit.");

        using var document = JsonDocument.Parse(responseLine);
        var response = document.RootElement;
        if (!response.TryGetProperty("requestId", out var responseId)
            || responseId.GetString() != requestId)
        {
            throw new InvalidDataException("The Java worker response correlation identifier is invalid.");
        }

        if (!response.TryGetProperty("status", out var status)
            || status.GetString() != "ok")
        {
            var code = response.TryGetProperty("error", out var error)
                && error.TryGetProperty("code", out var errorCode)
                    ? errorCode.GetString()
                    : "UNKNOWN_WORKER_ERROR";
            var message = response.TryGetProperty("error", out error)
                && error.TryGetProperty("message", out var errorMessage)
                    ? errorMessage.GetString()
                    : "The Java worker rejected the request.";
            throw new InvalidOperationException($"Java worker error {code}: {message}");
        }

        return response.GetProperty("result").Clone();
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

    private async Task StopWorkerAsync()
    {
        var process = _process;
        _process = null;
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and termination.
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Java semantic worker did not terminate within five seconds.");
        }
        finally
        {
            process.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await StopWorkerAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
