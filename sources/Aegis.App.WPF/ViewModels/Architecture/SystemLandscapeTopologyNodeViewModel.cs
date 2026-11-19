using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>A semantic node in the system-first landscape topology.</summary>
public sealed partial class SystemLandscapeTopologyNodeViewModel : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string? ParentRegionId { get; init; }
    public int HierarchyDepth { get; init; }
    public bool IsRegion { get; init; }
    public bool IsUnclassified { get; init; }
    public IReadOnlyList<string> LeafComponentIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SystemLandscapeRelationshipViewModel> InternalRelationships { get; internal set; } =
        Array.Empty<SystemLandscapeRelationshipViewModel>();
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isRelated;
    [ObservableProperty] private bool isDimmed;
    public double VisualOpacity => IsDimmed ? 0.42 : 1;
    partial void OnIsDimmedChanged(bool value) => OnPropertyChanged(nameof(VisualOpacity));

    public double Left { get; internal set; }
    public double Top { get; internal set; }
    public double Width { get; internal set; } = 220;
    public double Height { get; internal set; } = 76;
    public string AutomationName => $"{Category}: {Name}";
    public string AutomationHelpText => string.IsNullOrWhiteSpace(Detail)
        ? $"{Category} in the system architecture topology."
        : $"{Category}. {Detail}";
}
