namespace Aegis.Wpf.ViewModels.Architecture;

public sealed record SystemLandscapeCodeContainerViewModel(
    string Id,
    string Name,
    string Language,
    string ComponentKind,
    string ClassificationSource,
    double ClassificationConfidence,
    string ClassificationExplanation)
{
    public string ImplementationKind => $"{Language} · {ComponentKind}";
}
