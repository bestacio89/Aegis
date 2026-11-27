using System.Collections.ObjectModel;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>Immutable snapshot of static client-request to endpoint correlation results.</summary>
public sealed class ApiClientRequestEndpointMatchRegistry
{
    private readonly IReadOnlyDictionary<ApiClientRequestEndpointMatchStatus, IReadOnlyList<ApiClientRequestEndpointMatch>> _byStatus;
    private readonly IReadOnlyDictionary<int, ApiClientRequestEndpointMatch> _byRequestIndex;

    public ApiClientRequestEndpointMatchRegistry(IEnumerable<ApiClientRequestEndpointMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var snapshot = matches.ToArray();
        if (snapshot.Any(match => match is null))
            throw new ArgumentException("API match results cannot contain null entries.", nameof(matches));

        var duplicateRequestIndexes = snapshot
            .GroupBy(match => match.ClientRequestIndex)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateRequestIndexes.Length > 0)
        {
            throw new ArgumentException(
                $"Only one API match result is permitted per client request occurrence. Duplicate request indexes: {string.Join(", ", duplicateRequestIndexes)}.",
                nameof(matches));
        }

        Matches = Array.AsReadOnly(snapshot);
        _byStatus = new ReadOnlyDictionary<ApiClientRequestEndpointMatchStatus, IReadOnlyList<ApiClientRequestEndpointMatch>>(
            snapshot.GroupBy(match => match.Status).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ApiClientRequestEndpointMatch>)Array.AsReadOnly(group.ToArray())));
        _byRequestIndex = new ReadOnlyDictionary<int, ApiClientRequestEndpointMatch>(
            snapshot.ToDictionary(match => match.ClientRequestIndex));
    }

    public IReadOnlyList<ApiClientRequestEndpointMatch> Matches { get; }

    public int Count => Matches.Count;

    public IReadOnlyList<ApiClientRequestEndpointMatch> ForStatus(ApiClientRequestEndpointMatchStatus status) =>
        _byStatus.TryGetValue(status, out var matches) ? matches : Array.Empty<ApiClientRequestEndpointMatch>();

    public bool TryGetForRequest(int clientRequestIndex, out ApiClientRequestEndpointMatch? match) =>
        _byRequestIndex.TryGetValue(clientRequestIndex, out match);
}
