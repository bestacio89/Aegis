namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Describes one strongly connected dependency cluster. A cluster can contain
/// multiple distinct cycle paths; it is not presented as one concrete cycle.
/// </summary>
public sealed class ArchitectureDependencyCycleContext
{
    /// <summary>The graph projection in which the cycle was detected.</summary>
    public string Projection { get; init; } = string.Empty;

    /// <summary>Production or Test. These scopes are analysed independently.</summary>
    public string Scope { get; init; } = "Production";

    /// <summary>Deterministically ordered code-container or logical component IDs.</summary>
    public List<string> ComponentIds { get; init; } = [];

    /// <summary>Names corresponding by index to <see cref="ComponentIds"/>.</summary>
    public List<string> ComponentNames { get; init; } = [];

    /// <summary>Number of dependency edges contained in this strongly connected cluster.</summary>
    public int DependencyEdgeCount { get; init; }

    /// <summary>Number of source semantic relationships represented by those edges.</summary>
    public int RelationshipCount { get; init; }

    /// <summary>Exact source relationship references retained by the contributing edges.</summary>
    public List<SemanticRelationshipReference> ContributingRelationships { get; init; } = [];

    /// <summary>Relationships whose source provider omitted an ID.</summary>
    public int UnreferencedRelationshipCount { get; init; }

    /// <summary>True when at least one dependency edge is a self-dependency.</summary>
    public bool ContainsSelfDependency { get; init; }

    /// <summary>True when this cluster consists of one node with a self-dependency.</summary>
    public bool IsSingleNodeCycle { get; init; }
}
