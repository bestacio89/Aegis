namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Static projection of the declarative Gradle build-file subset understood by Aegis.
/// It never evaluates Gradle code or resolves artifacts.
/// </summary>
public sealed class GradleBuildModel
{
    public string BuildFile { get; init; } = string.Empty;

    public string Dialect { get; init; } = string.Empty;

    public List<Plugin> Plugins { get; init; } = [];

    public List<Dependency> Dependencies { get; init; } = [];

    public List<SourceSet> SourceSets { get; init; } = [];

    /// <summary>Literal custom Gradle Test task registrations; dynamic task configuration is not evaluated.</summary>
    public List<TestTask> TestTasks { get; init; } = [];

    public List<ProjectSourceRoot> SourceRoots { get; init; } = [];

    public string? JavaToolchainVersion { get; init; }

    public string? KotlinPluginVersion { get; init; }

    public string? KotlinLanguageVersion { get; init; }

    public string? KotlinApiVersion { get; init; }

    public string? KotlinJvmTarget { get; init; }

    public bool KotlinCompilerConfigurationInferred { get; init; }

    public List<AnalysisDiagnostic> Diagnostics { get; init; } = [];

    public bool IsComplete { get; init; }

    public sealed record Plugin(string Id, string? Version, bool IsApplied);

    public sealed record Dependency(
        string Configuration,
        string? Group,
        string? Artifact,
        string? Version,
        string? ProjectPath,
        string? CatalogAlias,
        bool IsPlatform,
        bool IsEnforcedPlatform,
        bool IsAnnotationProcessor,
        string Declaration);

    public sealed record SourceSet(
        string Name,
        List<string> JavaSourceDirectories,
        bool IsTest,
        List<string>? ExplicitJavaSourceDirectories = null,
        List<string>? KotlinSourceDirectories = null,
        List<string>? ExplicitKotlinSourceDirectories = null);

    public sealed record TestTask(
        string Name,
        string TaskType,
        string? SourceSetName,
        string Declaration);
}
