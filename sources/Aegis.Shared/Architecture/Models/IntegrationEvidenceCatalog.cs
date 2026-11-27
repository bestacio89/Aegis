using System.Collections.ObjectModel;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Immutable cross-domain index over the native messaging, RPC, and data-access
/// registries. It references original evidence and never merges or infers identities.
/// </summary>
public sealed class IntegrationEvidenceCatalog
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IntegrationEvidenceOccurrence>> _byLanguage;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IntegrationEvidenceOccurrence>> _byModule;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IntegrationEvidenceOccurrence>> _byProvider;
    private readonly IReadOnlyDictionary<ArchitectureRelationshipKind, IReadOnlyList<IntegrationEvidenceOccurrence>> _byRelationship;
    private readonly IReadOnlyDictionary<IntegrationEvidenceResolutionStatus, IReadOnlyList<IntegrationEvidenceOccurrence>> _byStatus;

    public IntegrationEvidenceCatalog(
        IntegrationEvidenceRegistry<MessageIntegrationEvidence>? messages,
        IntegrationEvidenceRegistry<RpcIntegrationEvidence>? rpc,
        IntegrationEvidenceRegistry<DataAccessEvidence>? dataAccess)
    {
        var occurrences = new List<IntegrationEvidenceOccurrence>();
        AddOccurrences(occurrences, IntegrationEvidenceDomain.Messaging, messages?.Evidence);
        AddOccurrences(occurrences, IntegrationEvidenceDomain.Rpc, rpc?.Evidence);
        AddOccurrences(occurrences, IntegrationEvidenceDomain.DataAccess, dataAccess?.Evidence);

        Evidence = Array.AsReadOnly(occurrences.ToArray());
        _byLanguage = CreateIndex(item => item.Evidence.Language, StringComparer.OrdinalIgnoreCase);
        _byModule = CreateIndex(item => item.Evidence.ModulePath, StringComparer.OrdinalIgnoreCase);
        _byProvider = CreateIndex(item => item.Evidence.ProviderId, StringComparer.Ordinal);
        _byRelationship = CreateIndex(item => item.Evidence.RelationshipKind);
        _byStatus = CreateIndex(item => item.Evidence.ResolutionStatus);
    }

    /// <summary>All occurrences in deterministic domain and source-registry order.</summary>
    public IReadOnlyList<IntegrationEvidenceOccurrence> Evidence { get; }

    public int Count => Evidence.Count;

    public IReadOnlyList<IntegrationEvidenceOccurrence> ForLanguage(string language) =>
        Find(_byLanguage, language);

    public IReadOnlyList<IntegrationEvidenceOccurrence> ForModule(string modulePath) =>
        Find(_byModule, modulePath);

    public IReadOnlyList<IntegrationEvidenceOccurrence> ForProvider(string providerId) =>
        Find(_byProvider, providerId);

    public IReadOnlyList<IntegrationEvidenceOccurrence> ForRelationship(ArchitectureRelationshipKind relationshipKind) =>
        _byRelationship.TryGetValue(relationshipKind, out var items) ? items : Array.Empty<IntegrationEvidenceOccurrence>();

    public IReadOnlyList<IntegrationEvidenceOccurrence> ForStatus(IntegrationEvidenceResolutionStatus status) =>
        _byStatus.TryGetValue(status, out var items) ? items : Array.Empty<IntegrationEvidenceOccurrence>();

    private IReadOnlyDictionary<TKey, IReadOnlyList<IntegrationEvidenceOccurrence>> CreateIndex<TKey>(
        Func<IntegrationEvidenceOccurrence, TKey> keySelector)
        where TKey : notnull =>
        CreateIndex(keySelector, EqualityComparer<TKey>.Default);

    private IReadOnlyDictionary<TKey, IReadOnlyList<IntegrationEvidenceOccurrence>> CreateIndex<TKey>(
        Func<IntegrationEvidenceOccurrence, TKey> keySelector,
        IEqualityComparer<TKey> comparer)
        where TKey : notnull =>
        new ReadOnlyDictionary<TKey, IReadOnlyList<IntegrationEvidenceOccurrence>>(
            Evidence.GroupBy(keySelector, comparer).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<IntegrationEvidenceOccurrence>)Array.AsReadOnly(group.ToArray()),
                comparer));

    private static IReadOnlyList<IntegrationEvidenceOccurrence> Find(
        IReadOnlyDictionary<string, IReadOnlyList<IntegrationEvidenceOccurrence>> index,
        string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return index.TryGetValue(key, out var items) ? items : Array.Empty<IntegrationEvidenceOccurrence>();
    }

    private static void AddOccurrences<T>(
        ICollection<IntegrationEvidenceOccurrence> target,
        IntegrationEvidenceDomain domain,
        IReadOnlyList<T>? evidence)
        where T : class, IIntegrationEvidence
    {
        if (evidence is null)
            return;

        for (var index = 0; index < evidence.Count; index++)
            target.Add(new IntegrationEvidenceOccurrence(domain, index, evidence[index]));
    }
}
