namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Describes one independently discovered project or package inside a repository.
/// Project identity and technology are kept separate from its inferred architecture layer.
/// </summary>
public sealed class ProjectModuleContext
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? ProjectFile { get; set; }

    public List<string> ProjectFiles { get; set; } = [];

    /// <summary>Semantic architecture layer inferred from the module identity.</summary>
    public string? Type { get; set; }

    /// <summary>Build/runtime project kind derived from project metadata.</summary>
    public string ProjectKind { get; set; } = "Unknown";

    public string? Framework { get; set; }

    public List<string> Frameworks { get; set; } = [];

    public List<string> TargetFrameworks { get; set; } = [];

    public List<string> Languages { get; set; } = [];

    public bool IsTestProject { get; set; }

    /// <summary>Identifies reusable utilities intended exclusively for tests.</summary>
    public bool IsTestSupport { get; set; }

    public bool IsWebProject { get; set; }

    public bool IsExecutable { get; set; }

    public bool IsLibrary { get; set; }

    public List<ProjectLayerContext> Layers { get; set; } = [];
}
