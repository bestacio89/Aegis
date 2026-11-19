using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// A normalized view of one provider-native relationship occurrence. The original
/// evidence remains authoritative; endpoint occurrences and identities are optional
/// when the provider evidence is missing, ambiguous, or lacks defensible ownership.
/// </summary>
public sealed record NormalizedSemanticRelationshipEvidence(
    SemanticRelationshipEvidence Evidence,
    ArchitectureRelationshipKind Kind,
    SemanticSymbolOccurrence? Source,
    SemanticSymbolOccurrence? Target,
    bool IsSourceAmbiguous,
    bool IsTargetAmbiguous)
{
    public SemanticSymbolIdentity? SourceIdentity => Source?.Identity;

    public SemanticSymbolIdentity? TargetIdentity => Target?.Identity;

    public SemanticRelationshipKind ProviderKind => Evidence.Relationship.Kind;

    public SemanticResolutionStatus ResolutionStatus => Evidence.Relationship.ResolutionStatus;
}
