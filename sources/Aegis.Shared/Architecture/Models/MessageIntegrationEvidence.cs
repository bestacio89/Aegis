using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>One source-level message publication or consumption observation.</summary>
public sealed class MessageIntegrationEvidence : IIntegrationEvidence
{
    public string EvidenceId { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Framework { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string ModulePath { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public MessageIntegrationOperation Operation { get; init; }
    public string? MessageTypeIdentity { get; init; }
    public string? MessageTypeExpression { get; init; }
    public MessageDestinationKind DestinationKind { get; init; }
    public string? DestinationIdentity { get; init; }
    public string? DestinationExpression { get; init; }
    public string? HandlerIdentity { get; init; }
    public IntegrationEvidenceResolutionStatus ResolutionStatus { get; init; }
    public IReadOnlyList<string> ResolutionEvidence { get; init; } = Array.Empty<string>();
    public string DeclarationEvidence { get; init; } = string.Empty;

    public ArchitectureRelationshipKind RelationshipKind => Operation switch
    {
        MessageIntegrationOperation.Publish => ArchitectureRelationshipKind.MessagePublish,
        MessageIntegrationOperation.Consume => ArchitectureRelationshipKind.MessageConsume,
        _ => ArchitectureRelationshipKind.Unknown
    };
}
