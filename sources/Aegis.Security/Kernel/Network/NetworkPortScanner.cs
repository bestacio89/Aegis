using System.Net.Sockets;

namespace Aegis.Security.Kernel.Network;

public sealed class NetworkPortScanner
{
    public async Task<PortScanResult> ScanAsync(
        string host,
        int port,
        int timeoutMs = 3000)
    {
        using var client = new TcpClient();
        var cts = new CancellationTokenSource(timeoutMs);
        var timer = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            timer.Stop();

            return new PortScanResult(
                Host: host,
                Port: port,
                IsOpen: true,
                LatencyMs: timer.ElapsedMilliseconds,
                Error: null
            );
        }
        catch (Exception ex)
        {
            timer.Stop();
            return new PortScanResult(
                Host: host,
                Port: port,
                IsOpen: false,
                LatencyMs: timer.ElapsedMilliseconds,
                Error: ex.Message
            );
        }
    }
}
