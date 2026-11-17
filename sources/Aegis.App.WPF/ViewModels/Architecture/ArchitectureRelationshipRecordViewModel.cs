namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// A ledger row projected from one authoritative unified graph edge. It retains
/// the graph domain, directed endpoints, and exact source evidence references.
/// </summary>
public sealed class ArchitectureRelationshipRecordViewModel
{
    public string GraphEdgeId { get; init; } = string.Empty;

    public string GraphDomain { get; init; } = string.Empty;

    public string RelationshipLevel { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string SourceLayer { get; init; } = string.Empty;

    public string SourceProjectPath { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string TargetLayer { get; init; } = string.Empty;

    public string TargetProjectPath { get; init; } = string.Empty;

    public string Direction { get; init; } = string.Empty;

    public string DependencyType { get; init; } = string.Empty;

    public string ProviderRelationshipKind { get; init; } = string.Empty;

    public string Scope { get; init; } = "Production";

    public string File { get; init; } = string.Empty;

    public int RelationshipCount { get; init; }

    public int ResolvedRelationshipCount { get; init; }

    public int PartiallyResolvedRelationshipCount { get; init; }

    public int UnreferencedRelationshipCount { get; init; }

    public bool IsInternal { get; init; }

    public string EvidenceReferences { get; init; } = string.Empty;

    public string CodeContainerContributions { get; init; } = string.Empty;
}
