using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Aegis.Wpf.ViewModels.Architecture;

public sealed partial class ArchitectureGraphEdgeViewModel : ObservableObject
{
    public ArchitectureGraphEdgeViewModel(
        string graphEdgeId,
        string domain,
        string scope,
        string relationshipKind,
        string source,
        string target,
        int relationshipCount,
        double left,
        double top,
        Geometry lineGeometry,
        Geometry arrowGeometry)
    {
        GraphEdgeId = graphEdgeId;
        Domain = domain;
        Scope = scope;
        RelationshipKind = relationshipKind;
        Source = source;
        Target = target;
        RelationshipCount = relationshipCount;
        Left = left;
        Top = top;
        LineGeometry = lineGeometry;
        ArrowGeometry = arrowGeometry;
    }

    public string GraphEdgeId { get; }
    public string Domain { get; }
    public string Scope { get; }
    public string RelationshipKind { get; }
    public string Source { get; }
    public string Target { get; }
    public int RelationshipCount { get; }
    public int EvidenceCount => RelationshipCount;
    public double Left { get; }
    public double Top { get; }
    public Geometry LineGeometry { get; }
    public Geometry ArrowGeometry { get; }
    public string EvidenceLabel => $"{Domain} · {Scope} · {RelationshipKind} · {RelationshipCount:N0} relationship(s)";

    [ObservableProperty]
    private bool isHighlighted;
}
