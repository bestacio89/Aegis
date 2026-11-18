using CommunityToolkit.Mvvm.ComponentModel;

namespace Aegis.Wpf.ViewModels.Architecture;

public sealed partial class ArchitectureGraphViewOptions : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DepthLabel))]
    private int depth = 1;

    public string DepthLabel => $"Package depth: {Depth}";

    partial void OnDepthChanging(int value)
    {
        if (value is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(value), "Package depth must be between 1 and 16.");
    }
}
