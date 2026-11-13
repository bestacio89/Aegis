namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Represents a logical architectural component, independent of language-level
/// code containers and build-system modules.
/// </summary>
public sealed class ArchitecturalComponentContext
{
    /// <summary>Stable identity within the analysed workspace.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable component name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional parent component identity for nested architecture maps.</summary>
    public string? ParentId { get; init; }

    /// <summary>Optional architectural role, such as Domain or Infrastructure.</summary>
    public string? Role { get; init; }

    public string? Description { get; init; }

    public int CodeContainerCount { get; init; }

    public int ProductionSymbolCount { get; init; }

    public int TestSymbolCount { get; init; }
}
