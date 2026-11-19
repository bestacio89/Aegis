using System.Collections.ObjectModel;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>Immutable, lossless snapshot of server endpoint observations.</summary>
public sealed class ServerApiEndpointDiscoveryRegistry
{
    private readonly IReadOnlyDictionary<ApiEndpointResolutionStatus, IReadOnlyList<ApiEndpointEvidence>> _byStatus;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ApiEndpointEvidence>> _byModule;

    public ServerApiEndpointDiscoveryRegistry(IEnumerable<ApiEndpointEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var snapshot = evidence.ToArray();
        if (snapshot.Any(item => item is null))
            throw new ArgumentException("Endpoint evidence cannot contain null entries.", nameof(evidence));

        Evidence = Array.AsReadOnly(snapshot);
        _byStatus = new ReadOnlyDictionary<ApiEndpointResolutionStatus, IReadOnlyList<ApiEndpointEvidence>>(
            snapshot.GroupBy(item => item.ResolutionStatus).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ApiEndpointEvidence>)Array.AsReadOnly(group.ToArray())));
        _byModule = new ReadOnlyDictionary<string, IReadOnlyList<ApiEndpointEvidence>>(
            snapshot.GroupBy(item => item.ModulePath, StringComparer.OrdinalIgnoreCase).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ApiEndpointEvidence>)Array.AsReadOnly(group.ToArray()),
                StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ApiEndpointEvidence> Evidence { get; }
    public int Count => Evidence.Count;

    public IReadOnlyList<ApiEndpointEvidence> ForStatus(ApiEndpointResolutionStatus status) =>
        _byStatus.TryGetValue(status, out var items) ? items : Array.Empty<ApiEndpointEvidence>();

    public IReadOnlyList<ApiEndpointEvidence> ForModule(string modulePath) =>
        _byModule.TryGetValue(modulePath, out var items) ? items : Array.Empty<ApiEndpointEvidence>();
}
