using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Single place that classifies security domains and probes as Static or Live.
/// The static pipeline runs only Static items; a Live item is never executed during a normal
/// folder scan. Anything not listed here is treated as Static.
///
/// Classification notes:
///  - Network, TLS and Cloud observe running systems.
///  - OS and Crypto inspect the machine the scan runs on (host state), not the audited project,
///    so they are Live: meaningful only when pointed at a host you are authorized to assess.
///  - WCF configuration files are Static. Fetching a service's metadata over the network is a
///    Live concern and belongs to the live pipeline.
/// </summary>
public static class SecurityModeCatalog
{
    private static readonly HashSet<string> LiveDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cloud", "Crypto", "Network", "OS", "TLS"
    };

    private static readonly HashSet<string> LiveProbeIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "net-port-probe"
    };

    public static ProbeMode ModeOfDomain(string domain) =>
        LiveDomains.Contains(domain) ? ProbeMode.Live : ProbeMode.Static;

    public static ProbeMode ModeOfProbe(string probeId) =>
        LiveProbeIds.Contains(probeId) ? ProbeMode.Live : ProbeMode.Static;
}