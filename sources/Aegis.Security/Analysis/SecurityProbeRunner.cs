using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Analysis;


public sealed class SecurityProbeRunner : ISecurityProbeRunner
{
    private readonly IEnumerable<ISecurityProbe> _probes;
    private readonly ILogger<SecurityProbeRunner> _logger;

    public SecurityProbeRunner(IEnumerable<ISecurityProbe> probes, ILogger<SecurityProbeRunner> logger)
    {
        _probes = probes;
        _logger = logger;
    }

    public async Task<IEnumerable<SecuritySignal>> RunAllAsync(ProjectSecurityContext ctx, CancellationToken ct = default)
    {
        var results = new List<SecuritySignal>();
        _logger.LogInformation("Starting security probe run for {Context}", ctx.ProjectPath);

        foreach (var probe in _probes)
        {
            try
            {
                _logger.LogDebug("Running probe {ProbeId}", probe.Id);
                var signals = await probe.ExecuteAsync(ctx, ct);
                results.AddRange(signals);
                _logger.LogDebug("Probe {ProbeId} emitted {Count} signals", probe.Id, signals.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Probe {ProbeId} failed", probe.Id);
            }
        }

        _logger.LogInformation("Completed {Count} probes", _probes.Count());
        return results;
    }
}
