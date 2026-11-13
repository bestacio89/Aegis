using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// A semantic relationship between declarations. Unresolved observations retain
/// their target description without fabricating a target symbol identity.
/// </summary>
public sealed class SemanticRelationship
{
    public string Id { get; init; } = string.Empty;

    public string SourceSymbolId { get; init; } = string.Empty;

    public string? TargetSymbolId { get; init; }

    public string? UnresolvedTarget { get; init; }

    public SemanticRelationshipKind Kind { get; init; }

    public SemanticResolutionStatus ResolutionStatus { get; init; }

    public bool IsTestCode { get; init; }

    public List<SemanticEvidenceProvenance> Provenance { get; init; } = [];
}
