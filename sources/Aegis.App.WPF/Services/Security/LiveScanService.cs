using Aegis.Security.Live;
using Aegis.Shared.Security.Live;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aegis.Wpf.Services.Security;

/// <summary>
/// Runs the live pipeline in its own DI scope (the pipeline and probes are scoped, WPF view models
/// are long-lived singletons) and produces the evidence file.
/// </summary>
public sealed class LiveScanService : ILiveScanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILiveScopeValidator _validator;
    private readonly TimeProvider _clock;
    private readonly ILogger<LiveScanService> _logger;

    public LiveScanService(
        IServiceScopeFactory scopeFactory,
        ILiveScopeValidator validator,
        TimeProvider clock,
        ILogger<LiveScanService> logger)
    {
        _scopeFactory = scopeFactory;
        _validator = validator;
        _clock = clock;
        _logger = logger;
    }

    public ScopeValidationResult Validate(LiveScanScope scope) =>
        _validator.Validate(scope, _clock.GetUtcNow());

    public async Task<LiveScanResult> RunAsync(
        LiveScanScope scope,
        IProgress<ProbeLogEntry> progress,
        CancellationToken ct)
    {
        using var diScope = _scopeFactory.CreateScope();
        var pipeline = diScope.ServiceProvider.GetRequiredService<ILiveProbePipeline>();

        _logger.LogInformation("Live probing started: scope {ScopeId}, {Count} target(s)", scope.Id, scope.Targets.Count);

        var result = await Task.Run(() => pipeline.RunAsync(scope, progress, ct), ct);

        _logger.LogInformation(
            "Live probing finished: {Results} observation(s), completed={Completed}, logIntact={Intact}",
            result.Results.Count, result.Completed, result.LogIntegrityVerified);

        return result;
    }

    public async Task ExportEvidenceAsync(LiveScanResult result, string outputPath, CancellationToken ct)
    {
        var evidence = new
        {
            schema = "aegis.live-evidence/1",
            exportedAtUtc = _clock.GetUtcNow(),
            scope = result.Scope,
            startedUtc = result.StartedUtc,
            completedUtc = result.CompletedUtc,
            completed = result.Completed,
            logIntegrityVerified = result.LogIntegrityVerified,
            blocked = result.Blocked,
            observations = result.Results,
            probeLog = result.Log
        };

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(evidence, JsonOptions), ct);

        _logger.LogInformation("Live evidence exported -> {Path}", outputPath);
    }
}