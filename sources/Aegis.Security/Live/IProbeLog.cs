using Aegis.Shared.Security.Live;

namespace Aegis.Security.Live;

public interface IProbeLog
{
    IReadOnlyList<ProbeLogEntry> Entries { get; }

    ProbeLogEntry Record(string probeId, string target, string action, string outcome, string? detail = null);

    /// <summary>Re-computes the hash chain and reports whether it is intact.</summary>
    bool VerifyIntegrity();
}