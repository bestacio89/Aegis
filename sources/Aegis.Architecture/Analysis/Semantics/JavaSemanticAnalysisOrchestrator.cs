using Aegis.Architecture.Analysis.Detectors;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Builds module-scoped Java semantic requests from discovered source-root ownership.
/// Build declarations are not treated as resolved classpath entries.
/// </summary>
public sealed class JavaSemanticAnalysisOrchestrator : ISemanticAnalysisProjectAnalyzer
{
    private readonly SemanticAnalysisCoordinator _coordinator;
    private readonly IJvmClasspathResolver _classpathResolver;
    private readonly ILogger<JavaSemanticAnalysisOrchestrator> _logger;
    private readonly JvmSourceRootResolver _sourceRootResolver;

    public string Language => "Java";

    public int ExecutionOrder => 200;

    public JavaSemanticAnalysisOrchestrator(
        IEnumerable<ISemanticAnalysisAdapter> adapters,
        IJvmClasspathResolver classpathResolver,
        ILogger<JavaSemanticAnalysisOrchestrator> logger,
        SemanticAnalysisCoordinator? coordinator = null,
        JvmSourceRootResolver? sourceRootResolver = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(classpathResolver);
        ArgumentNullException.ThrowIfNull(logger);

        _coordinator = coordinator ?? new SemanticAnalysisCoordinator(adapters);
        _classpathResolver = classpathResolver;
        _logger = logger;
        _sourceRootResolver = sourceRootResolver ?? new JvmSourceRootResolver();
    }

    public Task AnalyzeAsync(
        ProjectArchitectureContext context,
        CancellationToken cancellationToken = default) =>
        AnalyzeCoreAsync(context, null, cancellationToken);

    public Task AnalyzeAsync(
        ProjectArchitectureContext context,
        SemanticAnalysisExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        return AnalyzeCoreAsync(context, executionContext, cancellationToken);
    }

    private async Task AnalyzeCoreAsync(
        ProjectArchitectureContext context,
        SemanticAnalysisExecutionContext? executionContext,
        CancellationToken cancellationToken)
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

        if (modules.Count == 0
            && (context.Language.Equals("Java", StringComparison.OrdinalIgnoreCase)
                || context.Modules.Any(module => module.Languages.Any(language =>
                    language.Equals("Java", StringComparison.OrdinalIgnoreCase)))))
        {
            context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
            {
                Code = "JavaModulesNotSelected",
                Component = nameof(JavaSemanticAnalysisOrchestrator),
                Message = "Java was detected, but no Java module with an owned source scope could be selected for semantic analysis.",
                Target = context.RootPath,
                AffectsCoverage = true
            });
        }

        foreach (var module in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetUnsupportedAndroidTarget(module, out var androidPluginId))
            {
                context.SemanticAnalysisRuns.Add(CreateUnsupportedAndroidRun(module, androidPluginId));
                continue;
            }

            var sourceRoots = _sourceRootResolver.ResolveSourceRoots(module, "Java");
            if (sourceRoots.Count == 0)
            {
                context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "JavaSourceRootsNotFound",
                    Component = nameof(JavaSemanticAnalysisOrchestrator),
                    Message = "The Java module has no existing source roots; no source files could be analysed.",
                    Target = module.Path,
                    AffectsCoverage = true
                });
                continue;
            }

            // Unknown source ownership cannot be split safely. Preserve the
            // established combined analysis until discovery can classify it.
            if (sourceRoots.Any(root => root.Ownership == ProjectSourceRootOwnership.Unknown))
            {
                await AnalyzeScopeAsync(
                    module,
                    context,
                    sourceRoots,
                    includeTestCode: true,
                    includeTestDependencies: module.IsTestProject || module.IsTestSupport,
                    executionContext,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            IReadOnlyCollection<ProjectSourceRoot> productionRoots = module.IsTestProject || module.IsTestSupport
                ? Array.Empty<ProjectSourceRoot>()
                : sourceRoots
                    .Where(root => root.Ownership != ProjectSourceRootOwnership.Test)
                    .ToArray();
            IReadOnlyCollection<ProjectSourceRoot> testRoots = module.IsTestProject || module.IsTestSupport
                ? sourceRoots
                : sourceRoots
                    .Where(root => root.Ownership == ProjectSourceRootOwnership.Test)
                    .ToArray();

            await AnalyzeScopeAsync(
                module,
                context,
                productionRoots,
                includeTestCode: false,
                includeTestDependencies: false,
                executionContext,
                cancellationToken).ConfigureAwait(false);
            await AnalyzeScopeAsync(
                module,
                context,
                testRoots,
                includeTestCode: true,
                includeTestDependencies: true,
                executionContext,
                cancellationToken).ConfigureAwait(false);
        }

        JavaSemanticFrameworkDetector.Analyze(context);
        JavaSemanticDependencyDetector.Analyze(context);
        ModuleRoleDetector.Analyze(context.Modules);
    }

    private async Task AnalyzeScopeAsync(
        ProjectModuleContext module,
        ProjectArchitectureContext context,
        IReadOnlyCollection<ProjectSourceRoot> sourceRoots,
        bool includeTestCode,
        bool includeTestDependencies,
        SemanticAnalysisExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        if (sourceRoots.Count == 0)
            return;

        var sourceFiles = _sourceRootResolver.EnumerateSourceFiles(
            module.Path,
            sourceRoots,
            ".java",
            "Java",
            context.DiscoveryDiagnostics);
        if (sourceFiles.Count == 0)
        {
            context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
            {
                Code = "JavaSourceFilesNotFound",
                Component = nameof(JavaSemanticAnalysisOrchestrator),
                Message = $"The {(includeTestCode ? "test" : "production")} source scope contains no Java files.",
                Target = module.Path,
                AffectsCoverage = true
            });
            return;
        }

        var classpath = _classpathResolver.Resolve(module, context.Modules, includeTestCode);
        var dependencyPaths = JvmModuleDependencyResolver.ResolveDependencyPaths(
            module.Path,
            context.Modules,
            context.Dependencies,
            includeTestDependencies);
        var compilationOutputs = executionContext?.GetCompilationOutputs(
            module.Path,
            dependencyPaths.Production,
            dependencyPaths.Test,
            includeTestCode) ?? Array.Empty<string>();
        var request = new SemanticAnalysisRequest
        {
            Language = "Java",
            ProjectPath = Path.GetFullPath(module.Path),
            WorkspaceRoot = Path.GetFullPath(context.RootPath),
            ModuleName = module.Name,
            BuildSystem = module.BuildSystem.ToString(),
            SourceFiles = sourceFiles,
            SourceRoots = sourceRoots.Select(root => root.Directory).ToArray(),
            ReferencedSourceRoots = _sourceRootResolver.ResolveReferencedSourceRoots(
                    module, context, includeTestDependencies, "Java")
                .Concat(includeTestCode
                    ? _sourceRootResolver.ResolveOwnSourceRoots(module, context, "Java", includeTestCode: false)
                    : Array.Empty<string>())
                .Distinct(PathComparer)
                .OrderBy(path => path, PathComparer)
                .ToArray(),
            TestSourceRoots = includeTestCode
                ? (module.IsTestProject || module.IsTestSupport
                    ? sourceRoots.Select(root => root.Directory).ToArray()
                    : sourceRoots
                        .Where(root => root.Ownership == ProjectSourceRootOwnership.Test)
                        .Select(root => root.Directory)
                        .ToArray())
                : Array.Empty<string>(),
            GeneratedSourceRoots = sourceRoots
                .Where(root => root.IsGenerated)
                .Select(root => root.Directory)
                .ToArray(),
            // Keep resolver precedence intact; sorting can silently select a
            // different binary when classpath entries contain duplicate names.
            ClasspathEntries = classpath.Entries
                .Concat(compilationOutputs)
                .Distinct(PathComparer)
                .ToArray(),
            UnresolvedClasspathDependencies = classpath.UnresolvedDependencies,
            ClasspathResolutionComplete = classpath.IsComplete,
            BuildFiles = module.ProjectFiles.ToArray(),
            RequestedCapabilities = [],
            SourceLevel = ResolveSourceLevel(module),
            HasDeclaredDependencies = classpath.HasDeclaredDependencies,
            IncludeGeneratedCode = true,
            IncludeTestCode = includeTestCode
        };

        var run = await _coordinator.AnalyzeAsync(request, cancellationToken)
            .ConfigureAwait(false);
        context.SemanticAnalysisRuns.Add(run);

        _logger.LogInformation(
            "Java {SourceSet} analysis for module {ModuleName} completed with status {Status}: {SymbolCount} symbols, {RelationshipCount} relationships, {DiagnosticCount} diagnostics.",
            includeTestCode ? "test" : "production",
            module.Name,
            run.Status,
            run.Symbols.Count,
            run.Relationships.Count,
            run.Diagnostics.Count);
    }

    private static bool TryGetUnsupportedAndroidTarget(
        ProjectModuleContext module,
        out string pluginId)
    {
        if (module.GradleBuildModel?.Android is { } androidModel)
        {
            pluginId = string.IsNullOrWhiteSpace(androidModel.PluginId)
                ? "unresolved Android Gradle Plugin"
                : androidModel.PluginId;
            return true;
        }

        var androidPlugin = module.GradleBuildModel?.Plugins.FirstOrDefault(plugin =>
            plugin.IsApplied
            && (plugin.Id is
                "com.android.application"
                or "com.android.library"
                or "com.android.test"
                or "com.android.dynamic-feature"
                or "com.android.asset-pack"));

        pluginId = androidPlugin?.Id ?? string.Empty;
        return androidPlugin is not null;
    }

    private static SemanticAnalysisRun CreateUnsupportedAndroidRun(
        ProjectModuleContext module,
        string pluginId)
    {
        var now = DateTimeOffset.UtcNow;
        return new SemanticAnalysisRun
        {
            RunId = Guid.NewGuid().ToString("N"),
            Language = "Java",
            EngineId = nameof(JavaSemanticAnalysisOrchestrator),
            ProjectPath = module.Path,
            Status = SemanticAnalysisStatus.Unsupported,
            StartedAtUtc = now,
            CompletedAtUtc = now,
            Diagnostics =
            [
                new AnalysisDiagnostic
                {
                    Code = "JavaAndroidTargetNotSupported",
                    Component = nameof(JavaSemanticAnalysisOrchestrator),
                    Message = $"Android Java analysis is not enabled for '{pluginId}' until SDK platform classes, variant-specific classpaths and Android source ownership are modelled. The module will not be treated as a plain JVM project.",
                    Target = module.Path,
                    AffectsCoverage = true
                }
            ]
        };
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
