using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Aegis.Wpf.ViewModels.Architecture;

public sealed partial class ArchitectureGraphEdgeViewModel : ObservableObject
{
    public ArchitectureGraphEdgeViewModel(
        string source,
        string target,
        int evidenceCount,
        double left,
        double top,
        Geometry lineGeometry,
        Geometry arrowGeometry)
    {
        Source = source;
        Target = target;
        EvidenceCount = evidenceCount;
        Left = left;
        Top = top;
        LineGeometry = lineGeometry;
        ArrowGeometry = arrowGeometry;
    }

    public string Source { get; }

    public string Target { get; }

    public int EvidenceCount { get; }

    public double Left { get; }

    public double Top { get; }

    public Geometry LineGeometry { get; }

    public Geometry ArrowGeometry { get; }

    public string EvidenceLabel => EvidenceCount > 1
        ? $"{EvidenceCount} records"
        : string.Empty;

    [ObservableProperty]
    private bool isHighlighted;
}