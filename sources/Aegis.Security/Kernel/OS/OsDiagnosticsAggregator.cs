using Aegis.Security.Kernel.OS.Probes;
using Aegis.Security.Kernel.OS.Signals;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.OS;

public sealed class OsDiagnosticsAggregator
{
    private readonly OsInfoProbe _infoProbe = new();
    private readonly EnvironmentVariableProbe _envProbe = new();
    private readonly ProcessScannerProbe _procProbe = new();
    private readonly OsHardeningProbe _hardeningProbe = new();

    public IEnumerable<SecuritySignal> CollectAll()
    {
        var signals = new List<SecuritySignal>();

        // OS Info
        signals.Add(_infoProbe.Collect());

        // Environment Variables
        signals.AddRange(_envProbe.Collect());

        // Processes
        signals.AddRange(_procProbe.Scan());

        // Hardening
        signals.Add(_hardeningProbe.Probe());

        return signals;
    }
}
