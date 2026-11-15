using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Supervises one long-lived JVM JSONL worker. Language-specific clients
/// provide launch arguments and payloads; this class owns process lifecycle,
/// protocol framing, correlation, limits and graceful shutdown.
/// </summary>
internal sealed class JvmWorkerProcess : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger;
    private Process? _process;
    private JvmWorkerLaunchOptions? _activeOptions;
    private bool _disposed;

    public JvmWorkerProcess(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task StartAsync(JvmWorkerLaunchOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateOptions(options);

        if (_process is { HasExited: false })
            return;

        await StopAsync().ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in options.Arguments)
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                _logger.LogWarning("{WorkerName} stderr: {WorkerMessage}", options.WorkerName, eventArgs.Data);
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"{options.WorkerName} process could not be started.");

            _process = process;
            _activeOptions = options;
            process.BeginErrorReadLine();

            using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTimeout.CancelAfter(options.StartupTimeout);

            var handshake = await SendFrameAsync(process, options, "handshake", null, startupTimeout.Token)
                .ConfigureAwait(false);

            if (!handshake.TryGetProperty("protocolVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != options.ProtocolVersion)
            {
                throw new InvalidDataException(
                    $"{options.WorkerName} returned an incompatible protocol handshake.");
            }

            _logger.LogInformation(
                "Started {WorkerName} using protocol {ProtocolVersion}.",
                options.WorkerName,
                options.ProtocolVersion);
        }
        catch
        {
            if (_process is null)
                process.Dispose();
            else
                await StopAsync().ConfigureAwait(false);

            throw;
        }
    }

    public async Task<JsonElement> SendAsync(
        JvmWorkerLaunchOptions options,
        string operation,
        object? payload,
        CancellationToken cancellationToken)
    {
        await StartAsync(options, cancellationToken).ConfigureAwait(false);

        var process = _process
            ?? throw new InvalidOperationException($"{options.WorkerName} process is not running.");

        return await SendFrameAsync(process, options, operation, payload, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        var process = _process;
        var options = _activeOptions;
        _process = null;
        _activeOptions = null;

        if (process is null)
            return;

        try
        {
            if (!process.HasExited && options is not null)
            {
                using var shutdownTimeout = new CancellationTokenSource(ShutdownTimeout);
                try
                {
                    await SendFrameAsync(process, options, "shutdown", null, shutdownTimeout.Token)
                        .ConfigureAwait(false);
                    await process.WaitForExitAsync(shutdownTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning(
                        "{WorkerName} did not complete graceful shutdown within {Timeout}.",
                        options.WorkerName,
                        ShutdownTimeout);
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(
                        exception,
                        "{WorkerName} did not accept graceful shutdown; terminating the process tree.",
                        options.WorkerName);
                }
            }

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var terminationTimeout = new CancellationTokenSource(ShutdownTimeout);
                await process.WaitForExitAsync(terminationTimeout.Token).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and termination.
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "{WorkerName} process did not terminate within {Timeout}.",
                options?.WorkerName ?? "JVM worker",
                ShutdownTimeout);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not terminate {WorkerName} cleanly.",
                options?.WorkerName ?? "JVM worker");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task<JsonElement> SendFrameAsync(
        Process process,
        JvmWorkerLaunchOptions options,
        string operation,
        object? payload,
        CancellationToken cancellationToken)
    {
        if (process.HasExited)
            throw new InvalidOperationException($"{options.WorkerName} process has exited.");

        var requestId = Guid.NewGuid().ToString("N");
        var frame = JsonSerializer.Serialize(
            new { protocolVersion = options.ProtocolVersion, requestId, operation, payload },
            JsonOptions);

        if (Encoding.UTF8.GetByteCount(frame) > options.MaxFrameBytes)
            throw new InvalidDataException($"{options.WorkerName} request exceeds the configured frame limit.");

        await process.StandardInput.WriteLineAsync(frame.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

        var responseLine = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (responseLine is null)
            throw new EndOfStreamException($"{options.WorkerName} closed stdout before returning a response.");

        if (Encoding.UTF8.GetByteCount(responseLine) > options.MaxResponseBytes)
            throw new InvalidDataException($"{options.WorkerName} response exceeds the configured response limit.");

        using var document = JsonDocument.Parse(responseLine);
        var response = document.RootElement;

        if (!response.TryGetProperty("requestId", out var responseId)
            || !string.Equals(responseId.GetString(), requestId, StringComparison.Ordinal))
            throw new InvalidDataException($"{options.WorkerName} response correlation identifier is invalid.");

        if (!response.TryGetProperty("status", out var status)
            || !string.Equals(status.GetString(), "ok", StringComparison.Ordinal))
        {
            var hasError = response.TryGetProperty("error", out var error);
            var code = hasError && error.TryGetProperty("code", out var errorCode)
                ? errorCode.GetString()
                : "UNKNOWN_WORKER_ERROR";
            var message = hasError && error.TryGetProperty("message", out var errorMessage)
                ? errorMessage.GetString()
                : "The worker rejected the request.";
            throw new InvalidOperationException($"{options.WorkerName} error {code}: {message}");
        }

        if (!response.TryGetProperty("result", out var result))
            throw new InvalidDataException($"{options.WorkerName} response omitted its result payload.");

        return result.Clone();
    }

    private static void ValidateOptions(JvmWorkerLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkerName);
        ArgumentNullException.ThrowIfNull(options.Arguments);

        if (options.ProtocolVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Protocol version must be positive.");
        if (options.StartupTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Startup timeout must be positive.");
        if (options.MaxFrameBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum frame size must be positive.");
        if (options.MaxResponseBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum response size must be positive.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }
}
