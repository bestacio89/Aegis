using Aegis.Shared.Security.Models;

namespace Aegis.Shared.Security.Contracts;

/// <summary>
/// Executes all registered security probes once and returns the emitted signals.
/// This is the single-run execution path (CI/CD, manual analysis).
/// </summary>
public interface ISecurityProbeRunner
{
    Task<IEnumerable<SecuritySignal>> RunAllAsync(
        ProjectSecurityContext context,
        CancellationToken cancellationToken = default);
}
