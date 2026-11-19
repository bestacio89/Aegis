using System.Windows.Media;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>A directed, evidence-preserving topology connection.</summary>
public sealed class SystemLandscapeTopologyEdgeViewModel
{
    public string Id { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public int RelationshipCount { get; init; }
    public IReadOnlyList<string> LeafEdgeIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ArchitectureGraphEvidenceReference> Evidence { get; init; } =
        Array.Empty<ArchitectureGraphEvidenceReference>();
    public Geometry Route { get; internal set; } = Geometry.Empty;
    public Geometry ArrowHead { get; internal set; } = Geometry.Empty;
    public string AutomationName => $"{Domain} relationship from {SourceNodeId} to {TargetNodeId}";
    public string AutomationHelpText =>
        $"{Scope}; {RelationshipCount} relationship(s); {Evidence.Count} evidence occurrence(s).";
}