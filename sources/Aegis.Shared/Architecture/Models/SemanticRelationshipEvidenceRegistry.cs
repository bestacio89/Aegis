using System.Collections.ObjectModel;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Lossless, read-only index of every relationship observation in a semantic
/// analysis context. It deliberately does not filter by relationship kind,
/// resolution status, source/target completeness, provider ID, or run status.
/// </summary>
public sealed class SemanticRelationshipEvidenceRegistry
{
    private readonly IReadOnlyDictionary<string, SemanticRelationshipEvidence> _evidenceById;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<SemanticRelationshipEvidence>> _evidenceByRun;

    private SemanticRelationshipEvidenceRegistry(
        IReadOnlyList<SemanticRelationshipEvidence> evidence,
        IReadOnlyDictionary<string, SemanticRelationshipEvidence> evidenceById,
        IReadOnlyDictionary<int, IReadOnlyList<SemanticRelationshipEvidence>> evidenceByRun)
    {
        Evidence = evidence;
        _evidenceById = evidenceById;
        _evidenceByRun = evidenceByRun;
    }

    public IReadOnlyList<SemanticRelationshipEvidence> Evidence { get; }

    public int Count => Evidence.Count;

    public static SemanticRelationshipEvidenceRegistry Create(
        IEnumerable<SemanticAnalysisRun> semanticRuns)
    {
        ArgumentNullException.ThrowIfNull(semanticRuns);

        var evidence = new List<SemanticRelationshipEvidence>();
        var evidenceByRun = new Dictionary<int, IReadOnlyList<SemanticRelationshipEvidence>>();

        var runIndex = 0;
        foreach (var run in semanticRuns)
        {
            ArgumentNullException.ThrowIfNull(run);
            var runEvidence = new List<SemanticRelationshipEvidence>();
            var relationships = run.Relationships ?? [];

            for (var relationshipIndex = 0; relationshipIndex < relationships.Count; relationshipIndex++)
            {
                var relationship = relationships[relationshipIndex];
                if (relationship is null)
                {
                    throw new InvalidOperationException(
                        $"Semantic run at index {runIndex} contains a null relationship at index {relationshipIndex}.");
                }

                var item = new SemanticRelationshipEvidence(
                    runIndex,
                    relationshipIndex,
                    run.RunId,
                    run.Language,
                    run.EngineId,
                    run.ProjectPath,
                    relationship);

                evidence.Add(item);
                runEvidence.Add(item);
            }

            evidenceByRun.Add(runIndex, Array.AsReadOnly(runEvidence.ToArray()));
            runIndex++;
        }

        var evidenceArray = evidence.ToArray();
        var evidenceById = evidenceArray.ToDictionary(
            item => item.EvidenceId,
            StringComparer.Ordinal);

        return new SemanticRelationshipEvidenceRegistry(
            Array.AsReadOnly(evidenceArray),
            new ReadOnlyDictionary<string, SemanticRelationshipEvidence>(evidenceById),
            new ReadOnlyDictionary<int, IReadOnlyList<SemanticRelationshipEvidence>>(evidenceByRun));
    }

    public IReadOnlyList<SemanticRelationshipEvidence> ForRun(int runIndex) =>
        _evidenceByRun.TryGetValue(runIndex, out var evidence)
            ? evidence
            : Array.Empty<SemanticRelationshipEvidence>();

    public bool TryGet(string evidenceId, out SemanticRelationshipEvidence? evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceId);
        return _evidenceById.TryGetValue(evidenceId, out evidence);
    }
}
