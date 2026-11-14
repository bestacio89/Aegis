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
            .Where(root => root.ContainsLanguage(language))
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

        foreach (var sourceRoot in sourceRoots.Where(root => root.ContainsLanguage(language)))
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

        var modulePath = Path.GetFullPath(module.Path);
        var pending = new Stack<string>(
            context.Dependencies
                .Where(dependency => !string.IsNullOrWhiteSpace(dependency.SourceProjectPath)
                    && PathsEqual(dependency.SourceProjectPath, modulePath))
                .Where(dependency =>
                    dependency.Scope.Equals("Production", StringComparison.OrdinalIgnoreCase)
                    || (includeTestDependencies
                        && dependency.Scope.Equals("Test", StringComparison.OrdinalIgnoreCase)))
                .Select(dependency => dependency.TargetProjectPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(PathComparer)
                .OrderByDescending(path => path, PathComparer));

        var visitedModules = new HashSet<string>(PathComparer);
        var sourceRoots = new HashSet<string>(PathComparer);
        var workspaceRoot = Path.GetFullPath(context.RootPath);

        while (pending.TryPop(out var targetPath))
        {
            var normalizedTargetPath = Path.GetFullPath(targetPath);
            if (!visitedModules.Add(normalizedTargetPath))
                continue;

            var targetModules = context.Modules
                .Where(candidate => PathsEqual(candidate.Path, normalizedTargetPath))
                .Take(2)
                .ToArray();
            if (targetModules.Length != 1)
                continue;

            var targetModule = targetModules[0];
            foreach (var sourceRoot in ResolveSourceRoots(targetModule, language).Where(root =>
                         root.ContainsLanguage(language)
                         && IsSameOrDescendantPath(Path.GetFullPath(root.Directory), workspaceRoot)
                         && (root.Ownership == ProjectSourceRootOwnership.Production
                             || (includeTestDependencies && root.Ownership == ProjectSourceRootOwnership.Test))))
            {
                sourceRoots.Add(Path.GetFullPath(sourceRoot.Directory));
            }

            foreach (var dependency in context.Dependencies.Where(dependency =>
                         !string.IsNullOrWhiteSpace(dependency.SourceProjectPath)
                         && PathsEqual(dependency.SourceProjectPath, targetModule.Path)))
            {
                if (!dependency.Scope.Equals("Production", StringComparison.OrdinalIgnoreCase)
                    && !(includeTestDependencies
                        && dependency.Scope.Equals("Test", StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (!string.IsNullOrWhiteSpace(dependency.TargetProjectPath))
                    pending.Push(dependency.TargetProjectPath);
            }
        }

        return sourceRoots.OrderBy(path => path, PathComparer).ToArray();
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

    private static bool PathsEqual(string left, string right) =>
        PathComparer.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
