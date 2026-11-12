namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// A language-level architectural component discovered from semantic declarations.
/// Examples include Java/Kotlin packages, C# namespaces and source modules.
/// Build-module identity remains available separately.
/// </summary>
public sealed class ArchitectureComponentContext
{
    /// <summary>Stable identity within the analysed workspace.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Short component name suitable for graph labels.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Fully qualified language-level component identity.</summary>
    public string QualifiedName { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    /// <summary>Normalized language-level boundary kind, such as Package or Namespace.</summary>
    public string ComponentKind { get; init; } = string.Empty;

    public string BuildModuleName { get; init; } = string.Empty;

    public string BuildModulePath { get; init; } = string.Empty;

    public int SourceFileCount { get; init; }

    public int ProductionSourceFileCount { get; init; }

    public int TestSourceFileCount { get; init; }

    public int SymbolCount { get; init; }

    public int ProductionSymbolCount { get; init; }

    public int TestSymbolCount { get; init; }

    public List<string> SourceFiles { get; init; } = [];
}
