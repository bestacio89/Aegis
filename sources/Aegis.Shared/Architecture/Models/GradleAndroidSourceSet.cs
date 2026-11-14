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

    public GradleAndroidTestKind TestKind { get; init; }

    /// <summary>Variants to which this source set contributes according to static AGP naming rules.</summary>
    public List<string> AppliesToVariants { get; init; } = [];

    public List<string> JavaSourceDirectories { get; init; } = [];

    public List<string> KotlinSourceDirectories { get; init; } = [];

    public List<string> ExplicitJavaSourceDirectories { get; init; } = [];

    public List<string> ExplicitKotlinSourceDirectories { get; init; } = [];

    public List<string> GeneratedJavaSourceDirectories { get; init; } = [];

    public List<string> GeneratedKotlinSourceDirectories { get; init; } = [];

    public List<string> ManifestFiles { get; init; } = [];

    public List<string> ExplicitManifestFiles { get; init; } = [];

    public List<string> ResourceDirectories { get; init; } = [];

    public List<string> ExplicitResourceDirectories { get; init; } = [];

    public List<string> AssetDirectories { get; init; } = [];

    public List<string> ExplicitAssetDirectories { get; init; } = [];

    public string EvidenceFile { get; init; } = string.Empty;
}
