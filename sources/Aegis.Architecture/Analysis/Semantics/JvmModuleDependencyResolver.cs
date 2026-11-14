using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Resolves explicit JVM project-module dependency order and scope-aware
/// compilation-output inputs. It never infers dependencies from source names.
/// </summary>
internal static class JvmModuleDependencyResolver
{
    private const string MavenProjectDependency = "MavenProjectDependency";
    private const string GradleProjectDependency = "GradleProjectDependency";

    internal static IReadOnlyList<ProjectModuleContext> OrderModules(
        IReadOnlyCollection<ProjectModuleContext> modules,
        IReadOnlyCollection<ProjectModuleContext> allProjectModules,
        IReadOnlyCollection<ArchitectureDependencyContext> dependencies,
        bool includeTestDependencies,
        out IReadOnlyCollection<string> blockedModulePaths)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(allProjectModules);
        ArgumentNullException.ThrowIfNull(dependencies);

        var groups = modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .GroupBy(module => NormalizePath(module.Path), PathComparer)
            .ToArray();
        var duplicatePaths = groups
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToHashSet(PathComparer);
        var modulesByPath = groups
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), PathComparer);

        var dependencyPaths = modulesByPath.Keys.ToDictionary(
            path => path,
            _ => new HashSet<string>(PathComparer),
            PathComparer);
        var dependents = modulesByPath.Keys.ToDictionary(
            path => path,
            _ => new HashSet<string>(PathComparer),
            PathComparer);

        // Build the ordering graph from the complete project graph, not only
        // the language-specific subset. A Kotlin module may depend on another
        // Kotlin module through one or more Java modules.
        foreach (var modulePath in modulesByPath.Keys)
        {
            var resolvedPaths = ResolveDependencyPaths(
                modulePath,
                allProjectModules,
                dependencies,
                includeTestDependencies);
            var pathsToOrder = includeTestDependencies
                ? resolvedPaths.Test
                : resolvedPaths.Production;

            foreach (var dependencyPath in pathsToOrder)
            {
                var normalizedDependencyPath = NormalizePath(dependencyPath);
                if (!modulesByPath.ContainsKey(normalizedDependencyPath))
                    continue;

                if (dependencyPaths[modulePath].Add(normalizedDependencyPath))
                    dependents[normalizedDependencyPath].Add(modulePath);
            }
        }

        var remainingDependencies = dependencyPaths.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Count,
            PathComparer);
        var ready = new SortedSet<string>(
            remainingDependencies
                .Where(pair => pair.Value == 0)
                .Select(pair => pair.Key),
            PathComparer);
        var orderedPaths = new List<string>(modulesByPath.Count);

        while (ready.Count > 0)
        {
            var dependencyPath = ready.Min!;
            ready.Remove(dependencyPath);
            orderedPaths.Add(dependencyPath);

            foreach (var dependentPath in dependents[dependencyPath].OrderBy(path => path, PathComparer))
            {
                remainingDependencies[dependentPath]--;
                if (remainingDependencies[dependentPath] == 0)
                    ready.Add(dependentPath);
            }
        }

        var orderedPathSet = orderedPaths.ToHashSet(PathComparer);
        blockedModulePaths = duplicatePaths
            .Concat(modulesByPath.Keys.Where(path => !orderedPathSet.Contains(path)))
            .Distinct(PathComparer)
            .OrderBy(path => path, PathComparer)
            .ToArray();

        return orderedPaths.Select(path => modulesByPath[path]).ToArray();
    }

    /// <summary>
    /// Returns transitive project outputs required by a module. Production
    /// outputs are always included; test outputs are included only for
    /// explicitly identified test-project or test-support dependencies.
    /// </summary>
    public static (IReadOnlyList<string> Production, IReadOnlyList<string> Test) ResolveDependencyPaths(
        string modulePath,
        IReadOnlyCollection<ProjectModuleContext> projectModules,
        IReadOnlyCollection<ArchitectureDependencyContext> dependencies,
        bool includeTestDependencies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentNullException.ThrowIfNull(projectModules);
        ArgumentNullException.ThrowIfNull(dependencies);

        var modulesByPath = projectModules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .GroupBy(module => NormalizePath(module.Path), PathComparer)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), PathComparer);
        var rootPath = NormalizePath(modulePath);
        var edgesBySource = GetProjectEdges(dependencies, modulesByPath, includeTestDependencies)
            .GroupBy(edge => edge.SourcePath, PathComparer)
            .ToDictionary(
                group => group.Key,
                group => group
                    .ToArray(),
                PathComparer);

        var productionPaths = new List<string>();
        var testPaths = new List<string>();
        var seenProductionPaths = new HashSet<string>(PathComparer);
        var seenTestPaths = new HashSet<string>(PathComparer);
        var visitedProductionChain = new HashSet<string>(PathComparer);
        var visitedTestChain = new HashSet<string>(PathComparer);
        var pending = new Queue<(string Path, bool IncludeTestEdges)>();
        pending.Enqueue((rootPath, includeTestDependencies));

        while (pending.TryDequeue(out var current))
        {
            var visited = current.IncludeTestEdges
                ? visitedTestChain
                : visitedProductionChain;
            if (!visited.Add(current.Path)
                || !edgesBySource.TryGetValue(current.Path, out var outgoing))
            {
                continue;
            }

            foreach (var edge in outgoing)
            {
                if (seenProductionPaths.Add(edge.TargetPath))
                    productionPaths.Add(edge.TargetPath);

                if (edge.IsTestArtifact)
                {
                    if (seenTestPaths.Add(edge.TargetPath))
                        testPaths.Add(edge.TargetPath);
                    pending.Enqueue((edge.TargetPath, true));
                }
                else
                {
                    pending.Enqueue((edge.TargetPath, false));
                }
            }
        }

        return (productionPaths, testPaths);
    }

    private static IEnumerable<DependencyEdge> GetProjectEdges(
        IReadOnlyCollection<ArchitectureDependencyContext> dependencies,
        IReadOnlyDictionary<string, ProjectModuleContext> modulesByPath,
        bool includeTestDependencies)
    {
        foreach (var dependency in dependencies)
        {
            if (!string.Equals(dependency.DependencyType, MavenProjectDependency, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(dependency.DependencyType, GradleProjectDependency, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isProduction = string.Equals(dependency.Scope, "Production", StringComparison.OrdinalIgnoreCase);
            var isTest = string.Equals(dependency.Scope, "Test", StringComparison.OrdinalIgnoreCase);
            if (!isProduction && !(includeTestDependencies && isTest))
                continue;

            if (string.IsNullOrWhiteSpace(dependency.SourceProjectPath)
                || string.IsNullOrWhiteSpace(dependency.TargetProjectPath))
            {
                continue;
            }

            var sourcePath = NormalizePath(dependency.SourceProjectPath);
            var targetPath = NormalizePath(dependency.TargetProjectPath);
            var isTestArtifact = isTest
                && modulesByPath.TryGetValue(targetPath, out var targetModule)
                && IsTestArtifact(targetModule);

            yield return new DependencyEdge(
                sourcePath,
                targetPath,
                isTest ? "Test" : "Production",
                isTestArtifact);
        }
    }

    internal static bool IsTestArtifact(ProjectModuleContext module) =>
        module.IsTestProject || module.IsTestSupport;

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record DependencyEdge(
        string SourcePath,
        string TargetPath,
        string Scope,
        bool IsTestArtifact);
}
