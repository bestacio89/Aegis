using CommunityToolkit.Mvvm.ComponentModel;

namespace Aegis.Wpf.ViewModels.Architecture;

public sealed partial class ArchitectureGraphNodeViewModel : ObservableObject
{
    public const double NodeWidth = 196;
    public const double NodeHeight = 72;

    public ArchitectureGraphNodeViewModel(
        string id,
        string name,
        string layer,
        int incomingCount,
        int outgoingCount,
        double left,
        double top)
    {
        Id = id;
        Name = name;
        Layer = layer;
        IncomingCount = incomingCount;
        OutgoingCount = outgoingCount;
        Left = left;
        Top = top;
    }

    public string Id { get; }

    public string Name { get; }

    public string Layer { get; }

    public int IncomingCount { get; }

    public int OutgoingCount { get; }

    public int RelationshipCount => IncomingCount + OutgoingCount;

    public double Left { get; }

    public double Top { get; }

    [ObservableProperty]
    private bool isSelected;
}