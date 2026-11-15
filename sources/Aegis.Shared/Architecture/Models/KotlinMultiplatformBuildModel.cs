namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Static projection of the Kotlin Multiplatform target and source-set graph.
/// It records declarations only; it never configures or executes a Gradle build.
/// </summary>
public sealed class KotlinMultiplatformBuildModel
{
    public List<Target> Targets { get; init; } = [];

    public List<SourceSet> SourceSets { get; init; } = [];

    public List<AnalysisDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>How the source-set dependency graph was established.</summary>
    public string HierarchyMode { get; init; } = "Unknown";

    /// <summary>True when relevant Gradle configuration could not be statically interpreted.</summary>
    public bool HasDynamicConfiguration { get; init; }

    public bool IsComplete { get; init; }

    public sealed record Target(
        string Name,
        string Platform,
        string Declaration);

    public sealed record SourceSet(
        string Name,
        string Ownership,
        string Kind,
        string? TargetName,
        IReadOnlyList<string> DependsOn,
        string Declaration);
}
