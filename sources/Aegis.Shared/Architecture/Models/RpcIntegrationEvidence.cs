using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>One source-level RPC endpoint declaration or client invocation observation.</summary>
public sealed class RpcIntegrationEvidence : IIntegrationEvidence
{
    public string EvidenceId { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Framework { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string ModulePath { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public RpcIntegrationOperation Operation { get; init; }
    public string Protocol { get; init; } = string.Empty;
    public string? ServiceIdentity { get; init; }
    public string? MethodIdentity { get; init; }
    public string? TargetIdentity { get; init; }
    public string? TargetExpression { get; init; }
    public string? HandlerIdentity { get; init; }
    public IntegrationEvidenceResolutionStatus ResolutionStatus { get; init; }
    public IReadOnlyList<string> ResolutionEvidence { get; init; } = Array.Empty<string>();
    public string DeclarationEvidence { get; init; } = string.Empty;

    public ArchitectureRelationshipKind RelationshipKind => Operation switch
    {
        RpcIntegrationOperation.Call => ArchitectureRelationshipKind.RpcCall,
        RpcIntegrationOperation.Endpoint => ArchitectureRelationshipKind.RpcEndpoint,
        _ => ArchitectureRelationshipKind.Unknown
    };
}
