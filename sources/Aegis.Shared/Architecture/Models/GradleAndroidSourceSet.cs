using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Android source-set evidence discovered from conventional paths and supported
/// literal Android source-set declarations.
/// </summary>
public sealed record GradleAndroidSourceSet
{
    public string Name { get; init; } = string.Empty;

    public ProjectSourceRootOwnership Ownership { get; init; }

    public List<string> JavaSourceDirectories { get; init; } = [];

    public List<string> KotlinSourceDirectories { get; init; } = [];

    public List<string> ManifestFiles { get; init; } = [];

    public List<string> ResourceDirectories { get; init; } = [];

    public List<string> AssetDirectories { get; init; } = [];

    public string EvidenceFile { get; init; } = string.Empty;
}
