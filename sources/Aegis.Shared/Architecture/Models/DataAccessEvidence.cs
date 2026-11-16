using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>One source-level data read, write, or mapping observation.</summary>
public sealed class DataAccessEvidence : IIntegrationEvidence
{
    public string EvidenceId { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Framework { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string ModulePath { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public DataIntegrationOperation Operation { get; init; }
    public string Technology { get; init; } = string.Empty;
    public string? StoreIdentity { get; init; }
    public string? DataObjectIdentity { get; init; }
    public string? DataObjectExpression { get; init; }
    public string? SourceDataIdentity { get; init; }
    public string? TargetDataIdentity { get; init; }
    public string? QueryExpression { get; init; }
    public IntegrationEvidenceResolutionStatus ResolutionStatus { get; init; }
    public IReadOnlyList<string> ResolutionEvidence { get; init; } = Array.Empty<string>();
    public string DeclarationEvidence { get; init; } = string.Empty;

    public ArchitectureRelationshipKind RelationshipKind => Operation switch
    {
        DataIntegrationOperation.Read => ArchitectureRelationshipKind.DataRead,
        DataIntegrationOperation.Write => ArchitectureRelationshipKind.DataWrite,
        DataIntegrationOperation.Mapping => ArchitectureRelationshipKind.DataMapping,
        _ => ArchitectureRelationshipKind.Unknown
    };
}
