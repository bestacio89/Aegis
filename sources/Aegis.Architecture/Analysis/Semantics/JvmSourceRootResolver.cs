using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Resolves module-owned Java/Kotlin source roots and explicit in-workspace
/// source dependencies without executing build scripts.
/// </summary>
public sealed class JvmSourceRootResolver
{
    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".gradle", ".idea", "bin", "build", "node_modules", "obj", "out", "target"
        };

    public IReadOnlyList<ProjectSourceRoot> ResolveSourceRoots(
        ProjectModuleContext module,
        string language)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var roots = module.SourceRoots
            .Where(root => !string.IsNullOrWhiteSpace(root.Directory))
            .Where(root => Directory.Exists(root.Directory))
            .Where(root => root.ContainsLanguage(language) || root.Languages.Count == 0)
            .GroupBy(root => Path.GetFullPath(root.Directory), PathComparer)
            .Select(group => group.First() with { Directory = Path.GetFullPath(group.Key) })
            .ToList();

        if (roots.Count > 0)
            return roots;

        var sourceDirectoryName = language.Equals("Java", StringComparison.OrdinalIgnoreCase)
            ? "java"
            : language.Equals("Kotlin", StringComparison.OrdinalIgnoreCase)
                ? "kotlin"
                : throw new ArgumentOutOfRangeException(nameof(language), language, "Only JVM source languages are supported.");

        var modulePath = Path.GetFullPath(module.Path);
        var conventionalRoots = new[]
        {
            CreateConventionalRoot(modulePath, sourceDirectoryName, "main", language, ProjectSourceRootOwnership.Production),
            CreateConventionalRoot(modulePath, sourceDirectoryName, "test", language, ProjectSourceRootOwnership.Test)
        };

        roots.AddRange(conventionalRoots.Where(root => Directory.Exists(root.Directory)));
        if (roots.Count == 0
            && Directory.Exists(modulePath)
            && module.Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(new ProjectSourceRoot
            {
                Directory = modulePath,
                Ownership = ProjectSourceRootOwnership.Unknown,
                Origin = ProjectSourceRootOrigin.Observed,
                ContainsJavaSources = language.Equals("Java", StringComparison.OrdinalIgnoreCase),
                Languages = [language]
            });
        }

        return roots;
    }

    public IReadOnlyList<string> EnumerateSourceFiles(
        string modulePath,
        IReadOnlyCollection<ProjectSourceRoot> sourceRoots,
        string extension,
        string language,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentNullException.ThrowIfNull(sourceRoots);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
        var files = new HashSet<string>(PathComparer);
        var normalizedModulePath = Path.GetFullPath(modulePath);

        foreach (var sourceRoot in sourceRoots.Where(root => root.ContainsLanguage(language) || root.Languages.Count == 0))
        {
            var root = Path.GetFullPath(sourceRoot.Directory);
            if (!IsSameOrDescendantPath(root, normalizedModulePath))
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = $"{language}SourceRootOutsideModule",
                    Component = nameof(JvmSourceRootResolver),
                    Message = $"A {language} source root is outside its owning project module and was excluded.",
                    Target = root,
                    AffectsCoverage = true
                });
                continue;
            }

            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                        continue;

                    foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (!Path.GetExtension(file).Equals(normalizedExtension, StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                                files.Add(Path.GetFullPath(file));
                        }
                        catch (IOException exception)
                        {
                            diagnostics.Add(CreateDiscoveryDiagnostic(language, file, exception));
                        }
                        catch (UnauthorizedAccessException exception)
                        {
                            diagnostics.Add(CreateDiscoveryDiagnostic(language, file, exception));
                        }
                    }

                    foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (ExcludedDirectoryNames.Contains(Path.GetFileName(child)))
                            continue;

                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                                pending.Push(child);
                        }
                        catch (IOException exception)
                        {
                            diagnostics.Add(CreateDiscoveryDiagnostic(language, child, exception));
                        }
                        catch (UnauthorizedAccessException exception)
                        {
                            diagnostics.Add(CreateDiscoveryDiagnostic(language, child, exception));
                        }
                    }
                }
                catch (IOException exception)
                {
                    diagnostics.Add(CreateDiscoveryDiagnostic(language, directory, exception));
                }
                catch (UnauthorizedAccessException exception)
                {
                    diagnostics.Add(CreateDiscoveryDiagnostic(language, directory, exception));
                }
            }
        }

        return files.OrderBy(file => file, PathComparer).ToArray();
    }

    public IReadOnlyList<string> ResolveReferencedSourceRoots(
        ProjectModuleContext module,
        ProjectArchitectureContext context,
        bool includeTestDependencies,
        string language)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var moduleGroups = context.Modules
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Path))
            .GroupBy(candidate => NormalizePath(candidate.Path), PathComparer)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), PathComparer);
        var workspaceRoot = Path.GetFullPath(context.RootPath);
        var sourceRoots = new List<string>();
        var seenSourceRoots = new HashSet<string>(PathComparer);
        var visitedProductionModules = new HashSet<string>(PathComparer);
        var visitedTestArtifactModules = new HashSet<string>(PathComparer);
        var rootModulePath = NormalizePath(module.Path);
        visitedProductionModules.Add(rootModulePath);
        visitedTestArtifactModules.Add(rootModulePath);
        var pending = new Queue<(string Path, bool IncludeTestEdges, bool IncludeTestRoots)>();

        AddSourceRoots(module, includeTestDependencies);
        EnqueueDependencies(module.Path, includeTestDependencies);

        while (pending.TryDequeue(out var current))
        {
            var visited = current.IncludeTestEdges
                ? visitedTestArtifactModules
                : visitedProductionModules;
            if (!visited.Add(current.Path))
                continue;

            if (!moduleGroups.TryGetValue(current.Path, out var targetModule))
                continue;

            AddSourceRoots(targetModule, current.IncludeTestRoots);
            EnqueueDependencies(targetModule.Path, current.IncludeTestEdges);
        }

        return sourceRoots;

        void AddSourceRoots(ProjectModuleContext targetModule, bool includeTestRoots)
        {
            foreach (var sourceRoot in ResolveSourceRoots(targetModule, language))
            {
                if (!sourceRoot.ContainsLanguage(language))
                    continue;

                var normalizedRoot = Path.GetFullPath(sourceRoot.Directory);
                if (!IsSameOrDescendantPath(normalizedRoot, workspaceRoot))
                    continue;

                var isAllowedScope = sourceRoot.Ownership == ProjectSourceRootOwnership.Production
                    || (includeTestRoots
                        && sourceRoot.Ownership == ProjectSourceRootOwnership.Test);
                if (isAllowedScope && seenSourceRoots.Add(normalizedRoot))
                    sourceRoots.Add(normalizedRoot);
            }
        }

        void EnqueueDependencies(string sourcePath, bool allowTestEdges)
        {
            var normalizedSourcePath = NormalizePath(sourcePath);
            foreach (var dependency in context.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency.SourceProjectPath)
                    || !PathsEqual(dependency.SourceProjectPath, normalizedSourcePath)
                    || string.IsNullOrWhiteSpace(dependency.TargetProjectPath))
                {
                    continue;
                }

                var isProduction = string.Equals(
                    dependency.Scope,
                    "Production",
                    StringComparison.OrdinalIgnoreCase);
                var isTest = string.Equals(
                    dependency.Scope,
                    "Test",
                    StringComparison.OrdinalIgnoreCase);
                if (!isProduction && !(allowTestEdges && isTest))
                    continue;

                var targetPath = NormalizePath(dependency.TargetProjectPath);
                var isTestArtifact = isTest
                    && moduleGroups.TryGetValue(targetPath, out var dependencyModule)
                    && JvmModuleDependencyResolver.IsTestArtifact(dependencyModule);

                pending.Enqueue((
                    targetPath,
                    isTestArtifact,
                    isTestArtifact));
            }
        }
    }

    public IReadOnlyList<string> ResolveOwnSourceRoots(
        ProjectModuleContext module,
        ProjectArchitectureContext context,
        string language,
        bool includeTestCode)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        if ((module.IsTestProject || module.IsTestSupport) && !includeTestCode)
            return Array.Empty<string>();

        var workspaceRoot = Path.GetFullPath(context.RootPath);
        return ResolveSourceRoots(module, language)
            .Where(root => root.ContainsLanguage(language)
                && IsSameOrDescendantPath(Path.GetFullPath(root.Directory), workspaceRoot)
                && (root.Ownership == ProjectSourceRootOwnership.Production
                    || (includeTestCode
                        && (root.Ownership == ProjectSourceRootOwnership.Test
                            || root.Ownership == ProjectSourceRootOwnership.Unknown))))
            .Select(root => Path.GetFullPath(root.Directory))
            .Distinct(PathComparer)
            .OrderBy(path => path, PathComparer)
            .ToArray();
    }

    private static ProjectSourceRoot CreateConventionalRoot(
        string modulePath,
        string sourceDirectoryName,
        string sourceSet,
        string language,
        ProjectSourceRootOwnership ownership) =>
        new()
        {
            Directory = Path.Combine(modulePath, "src", sourceSet, sourceDirectoryName),
            Ownership = ownership,
            Origin = ProjectSourceRootOrigin.Conventional,
            ContainsJavaSources = language.Equals("Java", StringComparison.OrdinalIgnoreCase),
            Languages = [language]
        };

    private static AnalysisDiagnostic CreateDiscoveryDiagnostic(
        string language,
        string path,
        Exception exception) =>
        new()
        {
            Code = $"{language}SourceDiscoveryFailed",
            Component = nameof(JvmSourceRootResolver),
            Message = $"A {language} source directory could not be inspected; its files were not analysed.",
            Target = path,
            ExceptionType = exception.GetType().Name,
            AffectsCoverage = true
        };

    private static bool IsSameOrDescendantPath(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "."
            || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string left, string right) =>
        PathComparer.Equals(NormalizePath(left), NormalizePath(right));

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
