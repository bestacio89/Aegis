
namespace Aegis.Security.Kernel.Network;

public sealed record PortScanResult(
    string Host,
    int Port,
    bool IsOpen,
    long LatencyMs,
    string? Error
);
