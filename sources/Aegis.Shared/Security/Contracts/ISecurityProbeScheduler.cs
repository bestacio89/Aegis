// Aegis.Shared.Security.Contracts/ISecurityProbeScheduler.cs
using Aegis.Shared.Security.Models;

namespace Aegis.Shared.Security.Contracts;

/// <summary>
/// Manages continuous or interval-based execution of probes.
/// </summary>
public interface ISecurityProbeScheduler
{
    /// <summary>
    /// Starts realtime probing with a given refresh interval.
    /// Emits new <see cref="SecuritySignal"/>s as soon as probes produce them.
    /// </summary>
    Task StartAsync(ProjectSecurityContext context, TimeSpan interval, CancellationToken ct = default);

    /// <summary>Stops realtime monitoring.</summary>
    Task StopAsync(CancellationToken ct = default);
}
