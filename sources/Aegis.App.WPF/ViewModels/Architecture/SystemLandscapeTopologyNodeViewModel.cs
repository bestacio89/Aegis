using System.Windows;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>A semantic node in the system-first landscape topology.</summary>
public sealed class SystemLandscapeTopologyNodeViewModel
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
    public double Left { get; internal set; }
    public double Top { get; internal set; }
    public double Width { get; internal set; } = 220;
    public double Height { get; internal set; } = 76;
    public string AutomationName => $"{Category}: {Name}";
    public string AutomationHelpText => string.IsNullOrWhiteSpace(Detail)
        ? $"{Category} in the system architecture topology."
        : $"{Category}. {Detail}";
}