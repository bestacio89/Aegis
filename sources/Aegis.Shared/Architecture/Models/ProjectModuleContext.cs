using Aegis.Shared.Architecture.Enums;

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

    /// <summary>Build system owning this project module, independent of the repository aggregate.</summary>
    public ArchitectureBuildSystem BuildSystem { get; set; } = ArchitectureBuildSystem.Unknown;

    /// <summary>Semantic architecture layer inferred from the module identity.</summary>
    public string? Type { get; set; }

    /// <summary>Build/runtime project kind derived from project metadata.</summary>
    public string ProjectKind { get; set; } = "Unknown";

    public string? Framework { get; set; }

    public List<string> Frameworks { get; set; } = [];

    /// <summary>Framework observations attributed to this project module.</summary>
    public List<FrameworkEvidence> FrameworkEvidence { get; set; } = [];

    /// <summary>Statically interpreted Maven model; null for non-Maven projects.</summary>
    public MavenProjectModel? MavenModel { get; set; }

    /// <summary>Static Gradle settings/project graph; populated on the build root module.</summary>
    public GradleProjectModel? GradleModel { get; set; }

    /// <summary>Statically interpreted Gradle build declarations for this project module.</summary>
    public GradleBuildModel? GradleBuildModel { get; set; }

    /// <summary>Module-owned source roots with explicit production/test ownership and provenance.</summary>
    public List<ProjectSourceRoot> SourceRoots { get; set; } = [];

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
