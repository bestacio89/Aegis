using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Resolves explicit JVM project-module dependency order and scope-aware
/// compilation-output inputs. It never infers dependencies from source names.
/// </summary>
public static class JvmModuleDependencyResolver
{
    private const string MavenProjectDependency = "MavenProjectDependency";
    private const string GradleProjectDependency = "GradleProjectDependency";

    public static IReadOnlyList<ProjectModuleContext> OrderModules(
        IReadOnlyCollection<ProjectModuleContext> modules,
        IReadOnlyCollection<ArchitectureDependencyContext> dependencies,
        bool includeTestDependencies,
        out IReadOnlyCollection<string> blockedModulePaths)
    {
        ArgumentNullException.ThrowIfNull(modules);
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

        foreach (var edge in GetProjectEdges(dependencies, modulesByPath, includeTestDependencies))
        {
            // A test-scoped dependency on an ordinary module consumes that
            // module's production output, which is already available before
            // test analysis. Only explicit test artifacts need test ordering.
            if (edge.Scope.Equals("Test", StringComparison.OrdinalIgnoreCase)
                && !edge.IsTestArtifact)
            {
                continue;
            }

            if (!modulesByPath.ContainsKey(edge.SourcePath)
                || !modulesByPath.ContainsKey(edge.TargetPath)
                || PathsEqual(edge.SourcePath, edge.TargetPath))
            {
                continue;
            }

            if (dependencyPaths[edge.SourcePath].Add(edge.TargetPath))
                dependents[edge.TargetPath].Add(edge.SourcePath);
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
                    .OrderBy(edge => edge.TargetPath, PathComparer)
                    .ThenBy(edge => edge.Scope, StringComparer.Ordinal)
                    .ToArray(),
                PathComparer);

        var productionPaths = new HashSet<string>(PathComparer);
        var testPaths = new HashSet<string>(PathComparer);
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
                productionPaths.Add(edge.TargetPath);
                if (edge.IsTestArtifact)
                {
                    testPaths.Add(edge.TargetPath);
                    pending.Enqueue((edge.TargetPath, true));
                }
                else
                {
                    pending.Enqueue((edge.TargetPath, false));
                }
            }
        }

        return (
            productionPaths.OrderBy(path => path, PathComparer).ToArray(),
            testPaths.OrderBy(path => path, PathComparer).ToArray());
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
                && (targetModule.IsTestProject || targetModule.IsTestSupport);

            yield return new DependencyEdge(
                sourcePath,
                targetPath,
                isTest ? "Test" : "Production",
                isTestArtifact);
        }
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string left, string right) =>
        PathComparer.Equals(NormalizePath(left), NormalizePath(right));

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record DependencyEdge(
        string SourcePath,
        string TargetPath,
        string Scope,
        bool IsTestArtifact);
}
