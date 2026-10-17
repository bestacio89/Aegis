using Aegis.Shared.Security.Live;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Live;

/// <summary>
/// Live pipeline. Re-checks the scope before every probe, keeps probes within the authorized
/// intensity, enforces a per-probe timeout, spaces probes out, and logs every step.
/// It is the only component that may execute an <see cref="ILiveProbe"/>.
/// </summary>
public sealed class AegisLiveProbePipeline : ILiveProbePipeline
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DelayBetweenProbes = TimeSpan.FromMilliseconds(250);

    private readonly IEnumerable<ILiveProbe> _probes;
    private readonly ILiveScopeValidator _validator;
    private readonly TimeProvider _clock;
    private readonly ILogger<AegisLiveProbePipeline> _logger;

    public AegisLiveProbePipeline(
        IEnumerable<ILiveProbe> probes,
        ILiveScopeValidator validator,
        TimeProvider clock,
        ILogger<AegisLiveProbePipeline> logger)
    {
        _probes = probes;
        _validator = validator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<LiveScanResult> RunAsync(
        LiveScanScope scope,
        IProgress<ProbeLogEntry>? progress = null,
        CancellationToken ct = default)
    {
        var started = _clock.GetUtcNow();
        var log = new ProbeLog(_clock, entry => progress?.Report(entry));
        var results = new List<LiveProbeResult>();
        var blocked = new List<string>();
        var completed = false;

        // ---- gate ---------------------------------------------------------------------
        var validation = _validator.Validate(scope, started);
        if (!validation.IsValid)
        {
            foreach (var issue in validation.Issues)
            {
                blocked.Add(issue);
                log.Record("pipeline", "-", "scope-rejected", issue);
            }

            _logger.LogWarning("Live scan {ScopeId} rejected: {Count} scope issue(s).", scope.Id, validation.Issues.Count);
            return Build(scope, started, completed: false, results, log, blocked);
        }

        log.Record("pipeline", "-", "scope-accepted",
            $"scope={scope.Id}; auth='{scope.AuthorizationReference}' by '{scope.AuthorizedBy}'; " +
            $"targets={scope.Targets.Count}; window={scope.ValidFromUtc:u}..{scope.ValidUntilUtc:u}; maxIntensity={scope.MaxIntensity}");

        var eligible = new List<ILiveProbe>();
        foreach (var probe in _probes)
        {
            if (probe.Intensity <= scope.MaxIntensity)
            {
                eligible.Add(probe);
            }
            else
            {
                var reason = $"probe '{probe.Id}' needs {probe.Intensity} intensity, scope allows {scope.MaxIntensity}";
                blocked.Add(reason);
                log.Record(probe.Id, "-", "probe-skipped", reason);
            }
        }

        if (eligible.Count == 0)
            log.Record("pipeline", "-", "no-probes", "no live probe is eligible under this scope");

        // ---- run ----------------------------------------------------------------------
        try
        {
            foreach (var target in scope.Targets.DistinctBy(t => t.Display, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var probe in eligible)
                {
                    ct.ThrowIfCancellationRequested();

                    var now = _clock.GetUtcNow();
                    if (now > scope.ValidUntilUtc)
                    {
                        var reason = "the authorized window ended during the run; remaining probes were not executed";
                        blocked.Add(reason);
                        log.Record("pipeline", "-", "window-expired", reason);
                        return Build(scope, started, completed: false, results, log, blocked);
                    }

                    if (!_validator.IsTargetInScope(scope, target))
                    {
                        var reason = $"{target.Display} is not listed in the scope";
                        blocked.Add(reason);
                        log.Record(probe.Id, target.Display, "blocked", reason);
                        continue;
                    }

                    await RunProbeAsync(probe, target, log, results, ct);
                    await Task.Delay(DelayBetweenProbes, ct);
                }
            }

            completed = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            log.Record("pipeline", "-", "cancelled", "the run was stopped by the operator");
        }

        log.Record("pipeline", "-", "finished", $"{results.Count} result(s), completed={completed}");
        return Build(scope, started, completed, results, log, blocked);
    }

    private async Task RunProbeAsync(
        ILiveProbe probe,
        LiveTarget target,
        IProbeLog log,
        List<LiveProbeResult> results,
        CancellationToken ct)
    {
        log.Record(probe.Id, target.Display, "start", probe.Name);

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(ProbeTimeout);

            var produced = await probe.ExecuteAsync(target, log, linked.Token);
            results.AddRange(produced);

            log.Record(probe.Id, target.Display, "complete", $"{produced.Count} result(s)");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // operator cancellation: let the pipeline stop
        }
        catch (OperationCanceledException)
        {
            log.Record(probe.Id, target.Display, "timeout", $"no answer within {ProbeTimeout.TotalSeconds:0}s");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live probe {Probe} failed against {Target}", probe.Id, target.Display);
            log.Record(probe.Id, target.Display, "error", ex.Message);
        }
    }

    private LiveScanResult Build(
        LiveScanScope scope,
        DateTimeOffset started,
        bool completed,
        List<LiveProbeResult> results,
        ProbeLog log,
        List<string> blocked) =>
        new()
        {
            Scope = scope,
            StartedUtc = started,
            CompletedUtc = _clock.GetUtcNow(),
            Completed = completed,
            Results = results,
            Log = log.Entries,
            LogIntegrityVerified = log.VerifyIntegrity(),
            Blocked = blocked
        };
}