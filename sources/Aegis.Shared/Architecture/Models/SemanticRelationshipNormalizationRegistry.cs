using System.Collections.ObjectModel;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Immutable, derived normalization index over the lossless provider relationship
/// registry. It preserves every observation and never decides graph-edge eligibility.
/// </summary>
public sealed class SemanticRelationshipNormalizationRegistry
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<NormalizedSemanticRelationshipEvidence>> _byRun;
    private readonly IReadOnlyDictionary<ArchitectureRelationshipKind, IReadOnlyList<NormalizedSemanticRelationshipEvidence>> _byKind;

    private SemanticRelationshipNormalizationRegistry(
        IReadOnlyList<NormalizedSemanticRelationshipEvidence> evidence,
        IReadOnlyDictionary<int, IReadOnlyList<NormalizedSemanticRelationshipEvidence>> byRun,
        IReadOnlyDictionary<ArchitectureRelationshipKind, IReadOnlyList<NormalizedSemanticRelationshipEvidence>> byKind)
    {
        Evidence = evidence;
        _byRun = byRun;
        _byKind = byKind;
    }

    public IReadOnlyList<NormalizedSemanticRelationshipEvidence> Evidence { get; }

    public int Count => Evidence.Count;

    public static SemanticRelationshipNormalizationRegistry Create(
        SemanticRelationshipEvidenceRegistry relationshipRegistry,
        SemanticSymbolIndex symbolIndex)
    {
        ArgumentNullException.ThrowIfNull(relationshipRegistry);
        ArgumentNullException.ThrowIfNull(symbolIndex);

        var normalized = relationshipRegistry.Evidence
            .Select(item =>
            {
                var source = ResolveEndpoint(item, symbolIndex, item.Relationship.SourceSymbolId);
                var target = ResolveEndpoint(item, symbolIndex, item.Relationship.TargetSymbolId);

                return new NormalizedSemanticRelationshipEvidence(
                    item,
                    SemanticRelationshipKindMapper.Map(item.Relationship.Kind),
                    source.Occurrence,
                    target.Occurrence,
                    source.IsAmbiguous,
                    target.IsAmbiguous);
            })
            .ToArray();

        var byRun = normalized
            .GroupBy(item => item.Evidence.RunIndex)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<NormalizedSemanticRelationshipEvidence>)Array.AsReadOnly(group.ToArray()));

        var byKind = normalized
            .GroupBy(item => item.Kind)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<NormalizedSemanticRelationshipEvidence>)Array.AsReadOnly(group.ToArray()));

        return new SemanticRelationshipNormalizationRegistry(
            Array.AsReadOnly(normalized),
            new ReadOnlyDictionary<int, IReadOnlyList<NormalizedSemanticRelationshipEvidence>>(byRun),
            new ReadOnlyDictionary<ArchitectureRelationshipKind, IReadOnlyList<NormalizedSemanticRelationshipEvidence>>(byKind));
    }

    public IReadOnlyList<NormalizedSemanticRelationshipEvidence> ForRun(int runIndex) =>
        _byRun.TryGetValue(runIndex, out var evidence)
            ? evidence
            : Array.Empty<NormalizedSemanticRelationshipEvidence>();

    public IReadOnlyList<NormalizedSemanticRelationshipEvidence> ForKind(ArchitectureRelationshipKind kind) =>
        _byKind.TryGetValue(kind, out var evidence)
            ? evidence
            : Array.Empty<NormalizedSemanticRelationshipEvidence>();

    private static EndpointResolution ResolveEndpoint(
        SemanticRelationshipEvidence evidence,
        SemanticSymbolIndex symbolIndex,
        string? providerSymbolId)
    {
        if (string.IsNullOrWhiteSpace(providerSymbolId))
            return new EndpointResolution(null, false);

        var matches = symbolIndex.ForRun(evidence.RunIndex)
            .Where(occurrence => string.Equals(
                occurrence.Symbol.Id,
                providerSymbolId,
                StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            1 => new EndpointResolution(matches[0], false),
            > 1 => new EndpointResolution(null, true),
            _ => new EndpointResolution(null, false)
        };
    }

    private sealed record EndpointResolution(
        SemanticSymbolOccurrence? Occurrence,
        bool IsAmbiguous);
}
