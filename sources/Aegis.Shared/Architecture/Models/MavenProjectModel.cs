namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Static Maven project model derived from a POM and locally available parent
/// metadata. It never implies that Maven dependency artifacts were downloaded
/// or that active profiles/build plugins were executed.
/// </summary>
public sealed class MavenProjectModel
{
    public string ProjectFile { get; init; } = string.Empty;

    public Coordinates ProjectCoordinates { get; init; } = new(null, null, null);

    public Coordinates? ParentCoordinates { get; init; }

    public string? ParentProjectFile { get; init; }

    public string Packaging { get; init; } = "jar";

    public Dictionary<string, string> EffectiveProperties { get; init; } =
        new(StringComparer.Ordinal);

    public List<string> DeclaredModules { get; init; } = [];

    public List<string> ReactorModuleFiles { get; init; } = [];

    public List<Dependency> Dependencies { get; init; } = [];

    public List<Dependency> ManagedDependencies { get; init; } = [];

    public List<Plugin> Plugins { get; init; } = [];

    public List<Plugin> ManagedPlugins { get; init; } = [];

    public List<Dependency> AnnotationProcessorPaths { get; init; } = [];

    public List<ProjectSourceRoot> SourceRoots { get; init; } = [];

    public List<string> ProfileIds { get; init; } = [];

    public List<AnalysisDiagnostic> Diagnostics { get; init; } = [];

    public bool IsComplete { get; init; }

    public sealed record Coordinates(
        string? GroupId,
        string? ArtifactId,
        string? Version);

    public sealed record Exclusion(
        string? GroupId,
        string? ArtifactId);

    public sealed record Dependency(
        string? GroupId,
        string? ArtifactId,
        string? Version,
        string Scope,
        string Type,
        string? Classifier,
        bool Optional,
        bool IsInherited,
        bool IsVersionManaged,
        List<Exclusion> Exclusions,
        string SourceProjectFile,
        bool IsManaged);

    public sealed record Plugin(
        string? GroupId,
        string? ArtifactId,
        string? Version,
        bool IsInherited);
}
