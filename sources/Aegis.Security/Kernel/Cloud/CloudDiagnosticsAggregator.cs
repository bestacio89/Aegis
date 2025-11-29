using Aegis.Security.Kernel.Cloud.Probes;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.Cloud;

public sealed class CloudDiagnosticsAggregator
{
    private readonly CloudBucketProbe _bucket;
    private readonly CloudIamProbe _iam;
    private readonly CloudNetworkProbe _network;

    public CloudDiagnosticsAggregator(
        CloudBucketProbe bucket,
        CloudIamProbe iam,
        CloudNetworkProbe network)
    {
        _bucket = bucket;
        _iam = iam;
        _network = network;
    }

    public async Task<IEnumerable<SecuritySignal>> RunAsync(
        string provider,
        CancellationToken ct = default)
    {
        var signals = new List<SecuritySignal>();

        // For now we simulate — later this reads config
        signals.Add(await _bucket.ProbeAsync(provider, "bucket-1", ct));
        signals.Add(await _iam.ProbeAsync(provider, "principal-001", ct));
        signals.Add(await _network.ProbeAsync(provider, "vm-123", 22, ct));

        return signals;
    }
}
