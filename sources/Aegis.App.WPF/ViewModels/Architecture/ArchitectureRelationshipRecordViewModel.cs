namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// A presentation-neutral row that preserves the origin and evidence level of
/// either a build-module dependency or a semantic component dependency.
/// </summary>
public sealed class ArchitectureRelationshipRecordViewModel
{
    public string RelationshipLevel { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string SourceLayer { get; init; } = string.Empty;

    public string SourceProjectPath { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string TargetLayer { get; init; } = string.Empty;

    public string TargetProjectPath { get; init; } = string.Empty;

    public string DependencyType { get; init; } = string.Empty;

    public string Scope { get; init; } = "Production";

    public string File { get; init; } = string.Empty;

    public int RelationshipCount { get; init; } = 1;

    public int ResolvedRelationshipCount { get; init; }

    public int PartiallyResolvedRelationshipCount { get; init; }

    public int UnreferencedRelationshipCount { get; init; }

    public bool IsInternal { get; init; }

    public string EvidenceReferences { get; init; } = string.Empty;

    public string CodeContainerContributions { get; init; } = string.Empty;
}
