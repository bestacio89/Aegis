namespace Aegis.Shared.Security.Live;

/// <summary>Outcome of one live run: what was observed, what was refused, and the evidence log.</summary>
public sealed class LiveScanResult
{
    public required LiveScanScope Scope { get; init; }

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset CompletedUtc { get; init; }

    /// <summary>False when the run was cancelled, expired, or rejected before it finished.</summary>
    public bool Completed { get; init; }

    public IReadOnlyList<LiveProbeResult> Results { get; init; } = Array.Empty<LiveProbeResult>();

    public IReadOnlyList<ProbeLogEntry> Log { get; init; } = Array.Empty<ProbeLogEntry>();

    /// <summary>True when the log's hash chain re-verified after the run.</summary>
    public bool LogIntegrityVerified { get; init; }

    /// <summary>Reasons anything was refused or skipped (out of scope, window expired, scope rejected).</summary>
    public IReadOnlyList<string> Blocked { get; init; } = Array.Empty<string>();
}