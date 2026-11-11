namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Immutable-by-convention input describing the repository scope made available
/// to a semantic adapter. The request contains discovered paths and policy flags,
/// not parser-specific objects or executable build instructions.
/// </summary>
public sealed class SemanticAnalysisRequest
{
    public string Language { get; init; } = string.Empty;

    public string ProjectPath { get; init; } = string.Empty;

    public IReadOnlyList<string> SourceFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> BuildFiles { get; init; } = Array.Empty<string>();

    /// <summary>Stable capability identifiers requested from the selected adapter.</summary>
    public IReadOnlyList<string> RequestedCapabilities { get; init; } = Array.Empty<string>();

    public bool IncludeGeneratedCode { get; init; }

    public bool IncludeTestCode { get; init; }
}
