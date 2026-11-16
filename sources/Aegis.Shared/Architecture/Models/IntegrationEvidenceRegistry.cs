using System.Collections.ObjectModel;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Immutable, occurrence-preserving snapshot of one integration evidence domain.
/// Evidence IDs are lookup keys, not uniqueness constraints.
/// </summary>
public sealed class IntegrationEvidenceRegistry<T> where T : class, IIntegrationEvidence
{
    private readonly IReadOnlyDictionary<IntegrationEvidenceResolutionStatus, IReadOnlyList<T>> _byStatus;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<T>> _byModule;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<T>> _byEvidenceId;

    public IntegrationEvidenceRegistry(IEnumerable<T> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var snapshot = evidence.ToArray();
        if (snapshot.Any(item => item is null))
            throw new ArgumentException("Integration evidence cannot contain null entries.", nameof(evidence));

        Evidence = Array.AsReadOnly(snapshot);
        _byStatus = new ReadOnlyDictionary<IntegrationEvidenceResolutionStatus, IReadOnlyList<T>>(
            snapshot.GroupBy(item => item.ResolutionStatus).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<T>)Array.AsReadOnly(group.ToArray())));
        _byModule = new ReadOnlyDictionary<string, IReadOnlyList<T>>(
            snapshot.GroupBy(item => item.ModulePath, StringComparer.OrdinalIgnoreCase).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<T>)Array.AsReadOnly(group.ToArray()),
                StringComparer.OrdinalIgnoreCase));
        _byEvidenceId = new ReadOnlyDictionary<string, IReadOnlyList<T>>(
            snapshot.GroupBy(item => item.EvidenceId, StringComparer.Ordinal).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<T>)Array.AsReadOnly(group.ToArray()),
                StringComparer.Ordinal));
    }

    public IReadOnlyList<T> Evidence { get; }
    public int Count => Evidence.Count;

    public IReadOnlyList<T> ForStatus(IntegrationEvidenceResolutionStatus status) =>
        _byStatus.TryGetValue(status, out var items) ? items : Array.Empty<T>();

    public IReadOnlyList<T> ForModule(string modulePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        return _byModule.TryGetValue(modulePath, out var items) ? items : Array.Empty<T>();
    }

    /// <summary>Returns every occurrence with this provider-supplied ID; IDs are not assumed unique.</summary>
    public IReadOnlyList<T> ForEvidenceId(string evidenceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceId);
        return _byEvidenceId.TryGetValue(evidenceId, out var items) ? items : Array.Empty<T>();
    }
}
