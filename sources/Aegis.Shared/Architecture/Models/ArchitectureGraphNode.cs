using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>A deterministic node in the derived unified architecture graph.</summary>
public sealed record ArchitectureGraphNode
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ArchitectureGraphNodeKind Kind { get; init; }
    public string? Language { get; init; }
    public string? ComponentKind { get; init; }
    public string? ArchitecturalRole { get; init; }
    public string? ModulePath { get; init; }
    public string? ResourceProvider { get; init; }
    public string? ResourceIdentity { get; init; }
    public string? ResourceKind { get; init; }
    public bool IsTestScoped { get; init; }
}
