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

    /// <summary>Repository boundary used to validate explicitly referenced project source roots.</summary>
    public string WorkspaceRoot { get; init; } = string.Empty;

    public string ModuleName { get; init; } = string.Empty;

    public string BuildSystem { get; init; } = string.Empty;

    public IReadOnlyList<string> SourceFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SourceRoots { get; init; } = Array.Empty<string>();

    /// <summary>Source roots from explicitly declared project dependencies, used for compiler binding resolution only.</summary>
    public IReadOnlyList<string> ReferencedSourceRoots { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> TestSourceRoots { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GeneratedSourceRoots { get; init; } = Array.Empty<string>();

    /// <summary>Explicit, module-owned resolved artifacts. Declared coordinates are not classpath entries.</summary>
    public IReadOnlyList<string> ClasspathEntries { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> UnresolvedClasspathDependencies { get; init; } = Array.Empty<string>();

    public bool ClasspathResolutionComplete { get; init; } = true;

    public IReadOnlyList<string> BuildFiles { get; init; } = Array.Empty<string>();

    /// <summary>Namespaced, non-executable options interpreted only by the selected provider.</summary>
    public IReadOnlyDictionary<string, string> ProviderOptions { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Stable capability identifiers requested from the selected adapter.</summary>
    public IReadOnlyList<string> RequestedCapabilities { get; init; } = Array.Empty<string>();

    public string? SourceLevel { get; init; }

    public bool HasDeclaredDependencies { get; init; }

    public bool IncludeGeneratedCode { get; init; }

    public bool IncludeTestCode { get; init; }
}
