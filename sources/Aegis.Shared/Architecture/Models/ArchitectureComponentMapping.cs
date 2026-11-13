using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Explicitly maps a language-level code-container identity or build module
/// to a logical architectural component.
/// </summary>
public sealed class ArchitectureComponentMapping
{
    public string Id { get; init; } = string.Empty;

    public string ComponentId { get; init; } = string.Empty;

    public string ComponentName { get; init; } = string.Empty;

    public string? ComponentRole { get; init; }

    public string? Language { get; init; }

    public string? ModuleName { get; init; }

    public string? ModulePath { get; init; }

    public ArchitectureMappingMatchKind MatchKind { get; init; }

    /// <summary>
    /// Provider-native qualified identity for Exact/Prefix mappings, or the
    /// module identity for Module mappings.
    /// </summary>
    public string Value { get; init; } = string.Empty;

    public string? Description { get; init; }
}
