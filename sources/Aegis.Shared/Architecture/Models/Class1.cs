namespace Aegis.Shared.Architecture.Models;

public sealed class ProjectLayerContext
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;


    public List<string> Files { get; set; } = new();


    public double Confidence { get; set; }
}