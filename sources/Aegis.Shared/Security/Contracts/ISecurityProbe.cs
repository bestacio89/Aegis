using Aegis.Shared.Security.Models;

namespace Aegis.Shared.Security.Contracts;

/// <summary>
/// Low-level runtime component that collects live security evidence.
/// </summary>
public interface ISecurityProbe
{
    string Id { get; }
    string Name { get; }

    /// <summary>
    /// Executes the probe. Must never throw; errors are encoded as signals.
    /// </summary>
    Task<IEnumerable<SecuritySignal>> ExecuteAsync(
        ProjectSecurityContext context,
        CancellationToken cancellationToken = default);
}
