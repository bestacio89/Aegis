using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.Network.Signals;

public sealed record PortSecuritySignal : SecuritySignal
{
    public string Host { get; init; } = default!;
    public int Port { get; init; }
    public bool IsOpen { get; init; }
    public long LatencyMs { get; init; }
    public string? Error { get; init; }

    public PortSecuritySignal(PortScanResult result)
    {
        Host = result.Host;
        Port = result.Port;
        IsOpen = result.IsOpen;
        LatencyMs = result.LatencyMs;
        Error = result.Error;
    }
}
