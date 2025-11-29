namespace Aegis.Shared.Security.Models;

public sealed class NetworkProbeConfig
{
    /// <summary>
    /// IP or hostname to probe. Default is localhost.
    /// </summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>
    /// Ports to scan. If empty → defaults will be used by probes.
    /// </summary>
    public IReadOnlyCollection<int> Ports { get; init; } = Array.Empty<int>();

    /// <summary>
    /// TCP timeout in milliseconds.
    /// </summary>
    public int TimeoutMs { get; init; } = 2000;

    /// <summary>
    /// If true → probes run continuously instead of once.
    /// </summary>
    public bool Realtime { get; init; } = false;

    /// <summary>
    /// Interval between scans for realtime mode.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(15);
}
