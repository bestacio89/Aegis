using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// One relationship observation retained from a semantic analysis run. Run and
/// relationship indexes identify the exact occurrence even when provider IDs are
/// missing or duplicated. The indexes are scoped to the current context snapshot.
/// </summary>
public sealed record SemanticRelationshipEvidence(
    int RunIndex,
    int RelationshipIndex,
    string RunId,
    string Language,
    string EngineId,
    string ProjectPath,
    SemanticAnalysisStatus RunStatus,
    SemanticRelationship Relationship)
{
    /// <summary>Unique within one registry snapshot, not a cross-scan persistent ID.</summary>
    public string EvidenceId => $"{RunIndex}:{RelationshipIndex}";
}
