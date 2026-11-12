using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Builds module-scoped Java semantic requests from discovered source-root ownership.
/// Build declarations are not treated as resolved classpath entries.
/// </summary>
public sealed class JavaSemanticAnalysisOrchestrator
{
    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".gradle", ".idea", "bin", "build", "node_modules", "obj", "out", "target"
        };

    private readonly ISemanticAnalysisAdapter _adapter;
    private readonly IJavaClasspathResolver _classpathResolver;
    private readonly ILogger<JavaSemanticAnalysisOrchestrator> _logger;

    public JavaSemanticAnalysisOrchestrator(
        IEnumerable<ISemanticAnalysisAdapter> adapters,
        IJavaClasspathResolver classpathResolver,
        ILogger<JavaSemanticAnalysisOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(classpathResolver);
        ArgumentNullException.ThrowIfNull(logger);

        _adapter = adapters.FirstOrDefault(adapter =>
                adapter.Language.Equals("Java", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Java semantic analysis adapter is not registered.");
        _classpathResolver = classpathResolver;
        _logger = logger;
    }

    public async Task AnalyzeAsync(
        ProjectArchitectureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var modules = context.Modules
            .Where(module => IsJavaModule(module, context.Modules))
            .OrderBy(module => module.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (modules.Count == 0
            && context.Modules.Count == 0
            && context.Language.Equals("Java", StringComparison.OrdinalIgnoreCase))
        {
            modules.Add(new ProjectModuleContext
            {
                Name = context.ProjectName,
                Path = context.RootPath,
                Languages = ["Java"]
            });
        }

        foreach (var module in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceRoots = ResolveSourceRoots(module);
            if (sourceRoots.Count == 0)
                continue;

            var sourceFiles = EnumerateJavaFiles(
                module.Path,
                sourceRoots,
                context.DiscoveryDiagnostics);
            if (sourceFiles.Count == 0)
                continue;

            var classpath = _classpathResolver.Resolve(module, context.Modules, includeTestCode: true);
            var request = new SemanticAnalysisRequest
            {
                Language = "Java",
                ProjectPath = Path.GetFullPath(module.Path),
                ModuleName = module.Name,
                BuildSystem = module.BuildSystem.ToString(),
                SourceFiles = sourceFiles,
                SourceRoots = sourceRoots.Select(root => root.Directory).ToArray(),
                TestSourceRoots = sourceRoots
                    .Where(root => root.Ownership == ProjectSourceRootOwnership.Test)
                    .Select(root => root.Directory)
                    .ToArray(),
                GeneratedSourceRoots = sourceRoots
                    .Where(root => root.IsGenerated)
                    .Select(root => root.Directory)
                    .ToArray(),
                ClasspathEntries = classpath.Entries,
                UnresolvedClasspathDependencies = classpath.UnresolvedDependencies,
                ClasspathResolutionComplete = classpath.IsComplete,
                BuildFiles = module.ProjectFiles.ToArray(),
                RequestedCapabilities = _adapter.SupportedCapabilities.ToArray(),
                SourceLevel = ResolveSourceLevel(module),
                HasDeclaredDependencies = classpath.HasDeclaredDependencies,
                IncludeGeneratedCode = true,
                IncludeTestCode = true
            };

            if (!_adapter.CanAnalyze(request))
                continue;

            var run = await _adapter.AnalyzeAsync(request, cancellationToken)
                .ConfigureAwait(false);
            context.SemanticAnalysisRuns.Add(run);

            _logger.LogInformation(
                "Java semantic analysis for module {ModuleName} completed with status {Status}: {SymbolCount} symbols, {RelationshipCount} relationships, {DiagnosticCount} diagnostics.",
                module.Name,
                run.Status,
                run.Symbols.Count,
                run.Relationships.Count,
                run.Diagnostics.Count);
        }
    }

    private static bool IsJavaModule(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectModuleContext> modules)
    {
        var hasOwnedJavaRoots = module.SourceRoots.Any(root => root.ContainsJavaSources)
            || Directory.Exists(Path.Combine(module.Path, "src", "main", "java"))
            || Directory.Exists(Path.Combine(module.Path, "src", "test", "java"));
        var hasJavaLanguage = module.Languages.Any(language =>
            language.Equals("Java", StringComparison.OrdinalIgnoreCase));
        var ownsNestedModules = modules.Any(candidate =>
            !ReferenceEquals(candidate, module)
            && IsSameOrDescendantPath(Path.GetFullPath(candidate.Path), Path.GetFullPath(module.Path)));

        // An aggregator with child modules must not recursively absorb their
        // sources just because repository-level language detection saw Java.
        return hasOwnedJavaRoots || (hasJavaLanguage && !ownsNestedModules);
    }

    private static List<ProjectSourceRoot> ResolveSourceRoots(ProjectModuleContext module)
    {
        var roots = module.SourceRoots
            .Where(root => !string.IsNullOrWhiteSpace(root.Directory))
            .Where(root => Directory.Exists(root.Directory))
            .GroupBy(root => Path.GetFullPath(root.Directory), PathComparer)
            .Select(group => group.First() with { Directory = Path.GetFullPath(group.Key) })
            .ToList();

        if (roots.Count > 0)
            return roots;

        var modulePath = Path.GetFullPath(module.Path);
        var conventionalRoots = new[]
        {
            new ProjectSourceRoot
            {
                Directory = Path.Combine(modulePath, "src", "main", "java"),
                Ownership = ProjectSourceRootOwnership.Production,
                Origin = ProjectSourceRootOrigin.Conventional,
                ContainsJavaSources = true
            },
            new ProjectSourceRoot
            {
                Directory = Path.Combine(modulePath, "src", "test", "java"),
                Ownership = ProjectSourceRootOwnership.Test,
                Origin = ProjectSourceRootOrigin.Conventional,
                ContainsJavaSources = true
            }
        };

        roots.AddRange(conventionalRoots.Where(root => Directory.Exists(root.Directory)));
        if (roots.Count == 0 && Directory.Exists(modulePath))
        {
            roots.Add(new ProjectSourceRoot
            {
                Directory = modulePath,
                Ownership = ProjectSourceRootOwnership.Unknown,
                Origin = ProjectSourceRootOrigin.Observed,
                ContainsJavaSources = true
            });
        }

        return roots;
    }

    private static IReadOnlyList<string> EnumerateJavaFiles(
        string modulePath,
        IReadOnlyCollection<ProjectSourceRoot> sourceRoots,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var files = new HashSet<string>(PathComparer);
        var normalizedModulePath = Path.GetFullPath(modulePath);

        foreach (var sourceRoot in sourceRoots)
        {
            var root = Path.GetFullPath(sourceRoot.Directory);
            if (!IsSameOrDescendantPath(root, normalizedModulePath))
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "JavaSourceRootOutsideModule",
                    Component = nameof(JavaSemanticAnalysisOrchestrator),
                    Message = "A Java source root is outside its owning project module and was excluded.",
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
                        if (Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase))
                            files.Add(Path.GetFullPath(file));
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
                            diagnostics.Add(CreateDiscoveryDiagnostic(child, exception));
                        }
                        catch (UnauthorizedAccessException exception)
                        {
                            diagnostics.Add(CreateDiscoveryDiagnostic(child, exception));
                        }
                    }
                }
                catch (IOException exception)
                {
                    diagnostics.Add(CreateDiscoveryDiagnostic(directory, exception));
                }
                catch (UnauthorizedAccessException exception)
                {
                    diagnostics.Add(CreateDiscoveryDiagnostic(directory, exception));
                }
            }
        }

        return files.OrderBy(file => file, PathComparer).ToArray();
    }

    private static AnalysisDiagnostic CreateDiscoveryDiagnostic(string path, Exception exception) =>
        new()
        {
            Code = "JavaSourceDiscoveryFailed",
            Component = nameof(JavaSemanticAnalysisOrchestrator),
            Message = "A Java source directory could not be inspected; its files were not analysed.",
            Target = path,
            ExceptionType = exception.GetType().Name,
            AffectsCoverage = true
        };

    private static string ResolveSourceLevel(ProjectModuleContext module)
    {
        if (module.GradleBuildModel?.JavaToolchainVersion is { Length: > 0 } toolchain)
            return toolchain;

        if (module.MavenModel is { } maven)
        {
            foreach (var property in new[]
                     {
                         "maven.compiler.release",
                         "maven.compiler.source",
                         "maven.compiler.target",
                         "java.version"
                     })
            {
                if (maven.EffectiveProperties.TryGetValue(property, out var value)
                    && !string.IsNullOrWhiteSpace(value)
                    && !value.Contains("$" + "{", StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }

        return "21";
    }

    private static bool IsSameOrDescendantPath(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "."
            || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
