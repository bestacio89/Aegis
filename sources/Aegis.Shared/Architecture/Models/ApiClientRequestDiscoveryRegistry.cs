using System.Collections.ObjectModel;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>Immutable, lossless snapshot of outbound HTTP request observations.</summary>
public sealed class ApiClientRequestDiscoveryRegistry
{
    private readonly IReadOnlyDictionary<ApiClientRequestResolutionStatus, IReadOnlyList<ApiClientRequestEvidence>> _byStatus;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ApiClientRequestEvidence>> _byModule;

    public ApiClientRequestDiscoveryRegistry(IEnumerable<ApiClientRequestEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var snapshot = evidence.ToArray();
        if (snapshot.Any(item => item is null))
            throw new ArgumentException("Client request evidence cannot contain null entries.", nameof(evidence));

        Evidence = Array.AsReadOnly(snapshot);
        _byStatus = new ReadOnlyDictionary<ApiClientRequestResolutionStatus, IReadOnlyList<ApiClientRequestEvidence>>(
            snapshot.GroupBy(item => item.ResolutionStatus).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ApiClientRequestEvidence>)Array.AsReadOnly(group.ToArray())));
        _byModule = new ReadOnlyDictionary<string, IReadOnlyList<ApiClientRequestEvidence>>(
            snapshot.GroupBy(item => item.ModulePath, StringComparer.OrdinalIgnoreCase).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ApiClientRequestEvidence>)Array.AsReadOnly(group.ToArray()),
                StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ApiClientRequestEvidence> Evidence { get; }
    public int Count => Evidence.Count;

    public IReadOnlyList<ApiClientRequestEvidence> ForStatus(ApiClientRequestResolutionStatus status) =>
        _byStatus.TryGetValue(status, out var items) ? items : Array.Empty<ApiClientRequestEvidence>();

    public IReadOnlyList<ApiClientRequestEvidence> ForModule(string modulePath) =>
        _byModule.TryGetValue(modulePath, out var items) ? items : Array.Empty<ApiClientRequestEvidence>();
}
