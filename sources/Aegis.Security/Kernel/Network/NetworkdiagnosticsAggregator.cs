using Aegis.Shared.Security.Models;
using Aegis.Security.Kernel.Network.Signals;

namespace Aegis.Security.Kernel.Network;

public sealed class NetworkDiagnosticsAggregator
{
    private readonly NetworkPortScanner _scanner = new();

    public async Task<IEnumerable<SecuritySignal>> ScanHostAsync(
        string host,
        IEnumerable<int> ports,
        CancellationToken ct = default)
    {
        var tasks = ports
            .Select(port => _scanner.ScanAsync(host, port))
            .ToList();

        var results = await Task.WhenAll(tasks);

        return results.Select(r => new PortSecuritySignal(r));
    }

    public async Task<IEnumerable<SecuritySignal>> ScanMultipleHostsAsync(
        IEnumerable<string> hosts,
        IEnumerable<int> ports,
        CancellationToken ct = default)
    {
        var allSignals = new List<SecuritySignal>();

        foreach (var host in hosts)
        {
            var hostSignals = await ScanHostAsync(host, ports, ct);
            allSignals.AddRange(hostSignals);
        }

        return allSignals;
    }
}
