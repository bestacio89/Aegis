using Aegis.Security.Kernel.WCF.Probes;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.WCF;

public sealed class WcfDiagnosticsAggregator
{
    private readonly WcfSecurityProbe _probe;

    public WcfDiagnosticsAggregator(WcfSecurityProbe probe)
    {
        _probe = probe;
    }

    public IEnumerable<SecuritySignal> ScanDirectory(string rootPath)
    {
        var signals = new List<SecuritySignal>();

        foreach (var config in Directory.EnumerateFiles(rootPath, "*.config", SearchOption.AllDirectories))
        {
            signals.Add(_probe.ProbeFromConfig(config));
        }

        return signals;
    }
}
