namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Identifies one semantic relationship within the analysis run that produced it.
/// Relationship IDs are scoped to a run; the pair is the stable evidence reference
/// used by aggregate dependency projections.
/// </summary>
public sealed record SemanticRelationshipReference
{
    public string RunId { get; init; } = string.Empty;

    public string RelationshipId { get; init; } = string.Empty;
}
