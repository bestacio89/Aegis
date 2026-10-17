using Aegis.Shared.Security.Live;

namespace Aegis.Security.Live;

public interface ILiveProbePipeline
{
    /// <summary>
    /// Runs every eligible live probe against every in-scope target. Never throws for a rejected
    /// scope: the refusal is returned in <see cref="LiveScanResult.Blocked"/> and logged.
    /// </summary>
    Task<LiveScanResult> RunAsync(
        LiveScanScope scope,
        IProgress<ProbeLogEntry>? progress = null,
        CancellationToken ct = default);
}