namespace Aegis.Shared.Architecture.Models;

public sealed class ArchitectureBoundaryContext
{
    public string SourceLayer { get; set; } = string.Empty;

    public List<string> AllowedDependencies { get; set; } = new();

    public List<string> ForbiddenDependencies { get; set; } = new();
}