using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Converts explicit Maven and Gradle project dependencies into module graph
/// edges. Third-party package coordinates are never promoted to project edges.
/// </summary>
internal static class JavaProjectDependencyDetector
{
    private const string MavenDependencyType = "MavenProjectDependency";
    private const string GradleDependencyType = "GradleProjectDependency";

    public static void Analyze(ProjectArchitectureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Dependencies.RemoveAll(dependency =>
            dependency.DependencyType.Equals(MavenDependencyType, StringComparison.OrdinalIgnoreCase)
            || dependency.DependencyType.Equals(GradleDependencyType, StringComparison.OrdinalIgnoreCase));

        AnalyzeMavenDependencies(context);
        AnalyzeGradleDependencies(context);
        Deduplicate(context);
    }

    private static void AnalyzeMavenDependencies(ProjectArchitectureContext context)
    {
        foreach (var source in context.Modules.Where(module => module.MavenModel is not null))
        {
            var sourceModel = source.MavenModel!;
            foreach (var dependency in sourceModel.Dependencies)
            {
                if (string.Equals(dependency.Type, "pom", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dependency.Scope, "import", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(dependency.GroupId)
                    || string.IsNullOrWhiteSpace(dependency.ArtifactId))
                    continue;

                var candidates = context.Modules
                    .Where(candidate => !ReferenceEquals(candidate, source))
                    .Where(candidate => candidate.MavenModel is { } targetModel
                        && string.Equals(targetModel.ProjectCoordinates.GroupId, dependency.GroupId, StringComparison.Ordinal)
                        && string.Equals(targetModel.ProjectCoordinates.ArtifactId, dependency.ArtifactId, StringComparison.Ordinal)
                        && (string.IsNullOrWhiteSpace(dependency.Version)
                            || string.Equals(targetModel.ProjectCoordinates.Version, dependency.Version, StringComparison.Ordinal)))
                    .ToArray();

                // Ambiguous coordinates are not sufficient evidence to choose a target module.
                if (candidates.Length != 1)
                    continue;

                var target = candidates[0];
                AddDependency(
                    context,
                    source,
                    target,
                    MavenDependencyType,
                    sourceModel.ProjectFile,
                    string.Equals(dependency.Scope, "test", StringComparison.OrdinalIgnoreCase)
                        || source.IsTestProject
                        || source.IsTestSupport
                        || target.IsTestProject
                        || target.IsTestSupport);
            }
        }
    }

    private static void AnalyzeGradleDependencies(ProjectArchitectureContext context)
    {
        var projectReferences = context.Modules
            .Where(module => module.GradleModel is not null)
            .SelectMany(module => module.GradleModel!.Projects)
            .GroupBy(project => project.ProjectPath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var source in context.Modules.Where(module => module.GradleBuildModel is not null))
        {
            var sourceModel = source.GradleBuildModel!;
            foreach (var dependency in sourceModel.Dependencies)
            {
                if (dependency.IsPlatform
                    || dependency.IsEnforcedPlatform
                    || dependency.IsAnnotationProcessor
                    || string.IsNullOrWhiteSpace(dependency.ProjectPath)
                    || !projectReferences.TryGetValue(dependency.ProjectPath, out var references)
                    || references.Length != 1)
                    continue;

                var targetPath = references[0].ProjectDirectory;
                if (string.IsNullOrWhiteSpace(targetPath))
                    continue;

                var candidates = context.Modules
                    .Where(candidate => !ReferenceEquals(candidate, source))
                    .Where(candidate => PathsEqual(candidate.Path, targetPath))
                    .ToArray();
                if (candidates.Length != 1)
                    continue;

                var target = candidates[0];
                AddDependency(
                    context,
                    source,
                    target,
                    GradleDependencyType,
                    sourceModel.BuildFile,
                    dependency.Configuration.Contains("test", StringComparison.OrdinalIgnoreCase)
                        || source.IsTestProject
                        || source.IsTestSupport
                        || target.IsTestProject
                        || target.IsTestSupport);
            }
        }
    }

    private static void AddDependency(
        ProjectArchitectureContext context,
        ProjectModuleContext source,
        ProjectModuleContext target,
        string dependencyType,
        string? sourceFile,
        bool isTest)
    {
        if (PathsEqual(source.Path, target.Path))
            return;

        context.Dependencies.Add(new ArchitectureDependencyContext
        {
            Source = source.Name,
            SourceLayer = source.Layers.FirstOrDefault()?.Name ?? source.Type ?? "Unclassified",
            SourceProjectPath = Path.GetFullPath(source.Path),
            Target = target.Name,
            TargetLayer = target.Layers.FirstOrDefault()?.Name ?? target.Type ?? "Unclassified",
            TargetProjectPath = Path.GetFullPath(target.Path),
            DependencyType = dependencyType,
            Scope = isTest ? "Test" : "Production",
            File = string.IsNullOrWhiteSpace(sourceFile) ? string.Empty : Path.GetFullPath(sourceFile)
        });
    }

    private static void Deduplicate(ProjectArchitectureContext context)
    {
        var unique = context.Dependencies
            .GroupBy(
                dependency => $"{dependency.DependencyType}|{dependency.File}|{dependency.SourceProjectPath}|{dependency.TargetProjectPath}|{dependency.Scope}",
                PathComparer)
            .Select(group => group.First())
            .ToArray();
        context.Dependencies = unique.ToList();
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
