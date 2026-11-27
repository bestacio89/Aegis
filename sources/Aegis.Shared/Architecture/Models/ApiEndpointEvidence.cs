using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>One server-side HTTP endpoint declaration; identity is declaration-based, not route-based.</summary>
public sealed class ApiEndpointEvidence
{
    public string EvidenceId { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Framework { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string ModulePath { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public string HandlerIdentity { get; init; } = string.Empty;
    public IReadOnlyList<string> HttpMethods { get; init; } = Array.Empty<string>();
    public string? RouteTemplate { get; init; }
    public ApiEndpointResolutionStatus ResolutionStatus { get; init; }
    public IReadOnlyList<string> RouteCompositionEvidence { get; init; } = Array.Empty<string>();
    public string DeclarationEvidence { get; init; } = string.Empty;
}
