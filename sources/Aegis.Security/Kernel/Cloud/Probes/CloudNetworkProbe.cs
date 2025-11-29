using Aegis.Security.Kernel.Cloud.Signals;

namespace Aegis.Security.Kernel.Cloud.Probes;

public sealed class CloudNetworkProbe
{
    public async Task<CloudNetworkSecuritySignal> ProbeAsync(
        string provider,
        string resourceId,
        int port,
        CancellationToken ct = default)
    {
        try
        {
            await Task.Delay(20, ct);

            return new CloudNetworkSecuritySignal(
                provider,
                resourceId,
                port,
                protocol: "TCP",
                isOpenToWorld: port == 22 || port == 3389,
                error: null
            );
        }
        catch (Exception ex)
        {
            return new CloudNetworkSecuritySignal(
                provider,
                resourceId,
                port,
                protocol: "TCP",
                isOpenToWorld: false,
                error: ex.Message
            );
        }
    }
}
