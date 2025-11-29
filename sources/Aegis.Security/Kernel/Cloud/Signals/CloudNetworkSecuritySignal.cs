using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.Cloud.Signals;

public sealed record CloudNetworkSecuritySignal : SecuritySignal
{
    public string Provider { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Protocol { get; init; } = string.Empty;
    public bool IsOpenToWorld { get; init; }
    public string? Error { get; init; }

    public CloudNetworkSecuritySignal() { }

    public CloudNetworkSecuritySignal(
        string provider,
        string resourceId,
        int port,
        string protocol,
        bool isOpenToWorld,
        string? error = null)
    {
        Provider = provider;
        ResourceId = resourceId;
        Port = port;
        Protocol = protocol;
        IsOpenToWorld = isOpenToWorld;
        Error = error;

        Category = "Cloud";
        DetectorId = $"cloud:net:{resourceId}:{port}";
        Evidence = error ?? $"Network scan: {resourceId}:{port}";

        Metadata = new Dictionary<string, object>
        {
            ["provider"] = provider,
            ["resource"] = resourceId,
            ["port"] = port,
            ["protocol"] = protocol,
            ["open"] = isOpenToWorld
        };
    }
}
