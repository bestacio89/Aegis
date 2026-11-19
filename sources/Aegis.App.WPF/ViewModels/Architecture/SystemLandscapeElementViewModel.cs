namespace Aegis.Wpf.ViewModels.Architecture;

public sealed record SystemLandscapeElementViewModel(
    string Id,
    string Name,
    string Kind,
    string Detail,
    bool IsTestScoped);
