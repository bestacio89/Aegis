using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Analysis;

public sealed class SecurityRealtimeProbeRunner : ISecurityProbeScheduler, IDisposable
{
    private readonly ISecurityProbeRunner _probeRunner;
    private readonly IEnumerable<ISecurityEvaluator> _evaluators;   // ✔ NEW
    private readonly ISecurityAggregator _aggregator;
    private readonly ILogger<SecurityRealtimeProbeRunner> _logger;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public SecurityRealtimeProbeRunner(
        ISecurityProbeRunner probeRunner,
        IEnumerable<ISecurityEvaluator> evaluators,          // ✔ NEW
        ISecurityAggregator aggregator,
        ILogger<SecurityRealtimeProbeRunner> logger)
    {
        _probeRunner = probeRunner;
        _evaluators = evaluators;
        _aggregator = aggregator;
        _logger = logger;
    }

    public async Task StartAsync(
        ProjectSecurityContext context,
        TimeSpan interval,
        CancellationToken ct = default)
    {
        if (_cts != null)
        {
            _logger.LogWarning("Realtime probe runner already active.");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _logger.LogInformation("Realtime monitoring started (interval {Interval})", interval);

        await Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // 1. Run all probes → signals
                    var signals = await _probeRunner.RunAllAsync(context, _cts.Token);

                    // 2. Run all evaluators → evaluation results
                    var evaluations = new List<SecurityEvaluationResult>();

                    foreach (var evaluator in _evaluators)
                    {
                        var eval = await evaluator.EvaluateAsync(context, signals, _cts.Token);
                        evaluations.Add(eval);
                    }

                    // 3. Feed into aggregator
                    await _aggregator.AggregateAsync(evaluations, _cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Realtime probe cycle failed.");
                }

                await Task.Delay(interval, _cts.Token);
            }
        }, _cts.Token);
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        if (_cts == null)
        {
            _logger.LogInformation("Realtime probe runner not active.");
            return Task.CompletedTask;
        }

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;

        _logger.LogInformation("Realtime monitoring stopped.");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();

        _logger.LogInformation("Realtime probe runner disposed.");
    }
}
