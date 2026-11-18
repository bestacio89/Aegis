using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Projection;

/// <summary>A presentation-level package group retaining its exact leaf graph nodes.</summary>
public sealed record ArchitectureGraphRollupNode
{
    public string GroupKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Depth { get; init; }
    public IReadOnlyList<string> LeafComponentIds { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, int> InternalRelationshipCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
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
