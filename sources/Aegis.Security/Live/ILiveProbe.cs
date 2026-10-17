using Aegis.Shared.Security.Live;

namespace Aegis.Security.Live;

/// <summary>
/// A probe that observes a running target. It may only be invoked by the live pipeline,
/// after the scope gate has accepted the target.
/// </summary>
public interface ILiveProbe
{
    string Id { get; }

    string Name { get; }

    ProbeIntensity Intensity { get; }

    Task<IReadOnlyList<LiveProbeResult>> ExecuteAsync(LiveTarget target, IProbeLog log, CancellationToken ct);
}