using System.Text;
using System.Text.Json;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Owns one supervised Kotlin worker process and maps its bounded protocol
/// response into language-neutral semantic evidence.
/// </summary>
public sealed class KotlinWorkerClient : IKotlinWorkerClient, IAsyncDisposable
{
    private const int ProtocolVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly IOptionsMonitor<KotlinWorkerOptions> _options;
    private readonly ILogger<KotlinWorkerClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JvmWorkerProcess _workerProcess;
    private bool _disposed;

    public KotlinWorkerClient(
        IOptionsMonitor<KotlinWorkerOptions> options,
        ILogger<KotlinWorkerClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
        _workerProcess = new JvmWorkerProcess(logger);
    }

    public bool IsConfigured => _options.CurrentValue.IsConfigured;

    public async ValueTask<KotlinWorkerAnalysisResult> AnalyzeAsync(
        SemanticAnalysisRequest request,
        KotlinCompilerConfiguration configuration,
        string artifactRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var options = _options.CurrentValue;
            if (!options.IsConfigured)
                throw new InvalidOperationException(
                    "The Kotlin K2 worker is not deployed. Configure its worker JAR, pinned libraries and shared Java 21 runtime.");

            var launchOptions = CreateLaunchOptions(options);
            var normalizedArtifactRoot = Path.GetFullPath(artifactRoot);
            if (!Directory.Exists(normalizedArtifactRoot)
                || (File.GetAttributes(normalizedArtifactRoot) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "The Kotlin worker artifact root must be an existing host-owned directory.");
            }

            var sourceManifestPath = Path.Combine(normalizedArtifactRoot, "source-manifest.json");
            if (File.Exists(sourceManifestPath))
                throw new InvalidOperationException("The Kotlin worker artifact root already contains a source manifest.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.RequestTimeout);

            try
            {
                var sourceManifest = JsonSerializer.Serialize(request.SourceFiles, JsonOptions);
                await File.WriteAllTextAsync(
                    sourceManifestPath,
                    sourceManifest,
                    Utf8NoBom,
                    timeout.Token).ConfigureAwait(false);

                await _workerProcess.StartAsync(launchOptions, timeout.Token).ConfigureAwait(false);
                var startedAt = DateTimeOffset.UtcNow;
                var payload = new
                {
                    projectPath = request.ProjectPath,
                    workspaceRoot = request.WorkspaceRoot,
                    sourceRoots = request.SourceRoots,
                    referencedSourceRoots = request.ReferencedSourceRoots,
                    testSourceRoots = request.TestSourceRoots,
                    generatedSourceRoots = request.GeneratedSourceRoots,
                    sourceManifestPath,
                    artifactRoot = normalizedArtifactRoot,
                    classpathEntries = request.ClasspathEntries,
                    kotlinStandardLibraryPath = options.KotlinStandardLibraryPath,
                    compilerVersion = configuration.CompilerVersion,
                    languageVersion = configuration.LanguageVersion,
                    apiVersion = configuration.ApiVersion,
                    jvmTarget = configuration.JvmTarget,
                    moduleName = configuration.ModuleName,
                    includeTestCode = request.IncludeTestCode
                };

                var result = await _workerProcess.SendAsync(
                    launchOptions,
                    "analyze",
                    payload,
                    timeout.Token).ConfigureAwait(false);
                return MapResult(result, request, startedAt);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await _workerProcess.StopAsync().ConfigureAwait(false);
                throw new TimeoutException(
                    $"Kotlin K2 analysis exceeded the configured timeout of {options.RequestTimeout}.");
            }
            catch
            {
                await _workerProcess.StopAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                try
                {
                    File.Delete(sourceManifestPath);
                }
                catch (IOException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Could not remove temporary Kotlin source manifest {ManifestPath}.",
                        sourceManifestPath);
                }
                catch (UnauthorizedAccessException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Access to temporary Kotlin source manifest {ManifestPath} was denied during cleanup.",
                        sourceManifestPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsValidHeapSize(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
            return false;

        var suffix = char.ToLowerInvariant(value[^1]);
        return suffix is 'k' or 'm' or 'g'
            && long.TryParse(value.AsSpan(0, value.Length - 1), out var size)
            && size > 0;
    }

    private static JvmWorkerLaunchOptions CreateLaunchOptions(KotlinWorkerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.MaxHeapSize)
            || options.MaxHeapSize.Any(character => !char.IsAsciiLetterOrDigit(character))
            || !char.IsAsciiDigit(options.MaxHeapSize[0]))
        {
            throw new InvalidOperationException("The Kotlin worker maximum heap size is invalid.");
        }

        return new JvmWorkerLaunchOptions(
            options.JavaExecutablePath,
            AppContext.BaseDirectory,
            ["-Xms64m", $"-Xmx{options.MaxHeapSize}", "-jar", options.WorkerJarPath],
            ProtocolVersion,
            options.StartupTimeout,
            options.MaxFrameBytes,
            options.MaxResponseBytes,
            "Kotlin semantic worker");
    }

    private static KotlinWorkerAnalysisResult MapResult(
        JsonElement result,
        SemanticAnalysisRequest request,
        DateTimeOffset startedAt)
    {
        var statusValue = result.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.Number
                ? status.GetInt32()
                : (int)SemanticAnalysisStatus.Failed;
        var statusValueIsDefined = Enum.IsDefined(typeof(SemanticAnalysisStatus), statusValue);
        var run = new SemanticAnalysisRun
        {
            RunId = Guid.NewGuid().ToString("N"),
            Language = "Kotlin",
            EngineId = ReadString(result, "engineId") ?? "kotlin-k2-ir",
            EngineVersion = ReadString(result, "engineVersion"),
            ProjectPath = request.ProjectPath,
            Status = statusValueIsDefined
                ? (SemanticAnalysisStatus)statusValue
                : SemanticAnalysisStatus.Failed,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            AnalyzedFileCount = ReadInt32(result, "analyzedFileCount"),
            SkippedFileCount = ReadInt32(result, "skippedFileCount"),
            Capabilities = DeserializeArray<string>(result, "capabilities"),
            Symbols = DeserializeArray<SemanticSymbol>(result, "symbols"),
            Relationships = DeserializeArray<SemanticRelationship>(result, "relationships"),
            Diagnostics = DeserializeArray<AnalysisDiagnostic>(result, "diagnostics")
        };

        return new KotlinWorkerAnalysisResult(
            run,
            ReadString(result, "compilationOutputDirectory"),
            ReadBoolean(result, "compilationSucceeded"));
    }

    private static string? ReadString(JsonElement result, string propertyName) =>
        result.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt32(JsonElement result, string propertyName) =>
        result.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static bool ReadBoolean(JsonElement result, string propertyName) =>
        result.TryGetProperty(propertyName, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();

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
        }
    }
}
