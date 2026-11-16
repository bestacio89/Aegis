using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// One source-level outbound HTTP request observation. It is not proof that a
/// request executes or that it reaches a particular server endpoint.
/// </summary>
public sealed class ApiClientRequestEvidence
{
    public string EvidenceId { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Framework { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string ModulePath { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public string CallerIdentity { get; init; } = string.Empty;
    public string? HttpMethod { get; init; }
    public string? RequestUriExpression { get; init; }
    public string? BaseAddressExpression { get; init; }
    public ApiClientRequestResolutionStatus ResolutionStatus { get; init; }
    public IReadOnlyList<string> ResolutionEvidence { get; init; } = Array.Empty<string>();
    public string DeclarationEvidence { get; init; } = string.Empty;
}
