namespace Aegis.Shared.Architecture.Models;

public sealed class ProjectModuleContext
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? Type { get; set; }

    public List<string> Languages { get; set; } = new();

    public List<ProjectLayerContext> Layers { get; set; } = new();
}