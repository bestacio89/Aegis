using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Common provenance and resolution contract for source-level integration evidence.
/// Implementations retain domain-specific identities and expressions separately.
/// </summary>
public interface IIntegrationEvidence
{
    string EvidenceId { get; }
    string Language { get; }
    string Framework { get; }
    string ProviderId { get; }
    string ModulePath { get; }
    string SourceFile { get; }
    int StartLine { get; }
    int StartColumn { get; }
    ArchitectureRelationshipKind RelationshipKind { get; }
    IntegrationEvidenceResolutionStatus ResolutionStatus { get; }
    IReadOnlyList<string> ResolutionEvidence { get; }
    string DeclarationEvidence { get; }
}
