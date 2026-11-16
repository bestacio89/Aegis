namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Exact reference to one relationship observation within a semantic context
/// snapshot. Provider IDs remain available for compatibility, while the indexes
/// disambiguate duplicate or omitted provider IDs.
/// </summary>
public sealed record SemanticRelationshipReference
{
    public string RunId { get; init; } = string.Empty;

    public string RelationshipId { get; init; } = string.Empty;

    /// <summary>Zero-based position of the run in the source context snapshot.</summary>
    public int RunIndex { get; init; }

    /// <summary>Zero-based position of the relationship within its run.</summary>
    public int RelationshipIndex { get; init; }
}
