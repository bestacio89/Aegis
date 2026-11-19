using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>A directed, evidence-preserving topology connection.</summary>
public sealed partial class SystemLandscapeTopologyEdgeViewModel : ObservableObject
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
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isHighlighted;
    [ObservableProperty] private bool isDimmed;

    public double StrokeOpacity => IsSelected ? 1 : IsHighlighted ? 0.9 : IsDimmed ? 0.12 : 0.55;
    public double ArrowOpacity => IsSelected || IsHighlighted ? 1 : IsDimmed ? 0.12 : 0.75;
    public double StrokeThickness => IsSelected ? 2.8 : IsHighlighted ? 2.1 : 1.4;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(StrokeOpacity));
        OnPropertyChanged(nameof(StrokeThickness));
    }
    partial void OnIsHighlightedChanged(bool value)
    {
        OnPropertyChanged(nameof(StrokeOpacity));
        OnPropertyChanged(nameof(ArrowOpacity));
        OnPropertyChanged(nameof(StrokeThickness));
    }
    partial void OnIsDimmedChanged(bool value)
    {
        OnPropertyChanged(nameof(StrokeOpacity));
        OnPropertyChanged(nameof(ArrowOpacity));
    }

    public Geometry Route { get; internal set; } = Geometry.Empty;
    public Geometry ArrowHead { get; internal set; } = Geometry.Empty;
    public string AutomationName => $"{Domain} relationship from {SourceNodeId} to {TargetNodeId}";
    public string AutomationHelpText =>
        $"{Scope}; {RelationshipCount} relationship(s); {Evidence.Count} evidence occurrence(s).";
}
