namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Static Gradle settings model. It captures only literal settings declarations;
/// no Gradle scripts, plugins, init scripts or included builds are executed.
/// </summary>
public sealed class GradleProjectModel
{
    public string SettingsFile { get; init; } = string.Empty;

    public string RootDirectory { get; init; } = string.Empty;

    public string? RootProjectName { get; init; }

    public List<ProjectReference> Projects { get; init; } = [];

    public List<IncludedBuild> IncludedBuilds { get; init; } = [];

    public List<string> VersionCatalogFiles { get; init; } = [];

    public List<AnalysisDiagnostic> Diagnostics { get; init; } = [];

    public bool IsComplete { get; set; }

    public sealed record ProjectReference(
        string ProjectPath,
        string ProjectDirectory,
        string? BuildFile,
        bool IsRoot);

    public sealed record IncludedBuild(
        string BuildPath,
        string Directory,
        bool IsDiscovered,
        string? SettingsFile,
        string? RootProjectName);
}
