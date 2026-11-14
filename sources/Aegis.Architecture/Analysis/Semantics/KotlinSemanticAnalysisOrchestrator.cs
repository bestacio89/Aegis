using Aegis.Architecture.Analysis.Detectors;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Builds module-scoped Kotlin JVM semantic requests from statically discovered
/// Kotlin source-set ownership and explicit JVM dependency evidence.
/// </summary>
public sealed class KotlinSemanticAnalysisOrchestrator : ISemanticAnalysisProjectAnalyzer
{
    private readonly SemanticAnalysisCoordinator _coordinator;
    private readonly IJvmClasspathResolver _classpathResolver;
    private readonly ILogger<KotlinSemanticAnalysisOrchestrator> _logger;
    private readonly JvmSourceRootResolver _sourceRootResolver;

    public string Language => "Kotlin";

    public int ExecutionOrder => 100;

    public KotlinSemanticAnalysisOrchestrator(
        IEnumerable<ISemanticAnalysisAdapter> adapters,
        IJvmClasspathResolver classpathResolver,
        ILogger<KotlinSemanticAnalysisOrchestrator> logger,
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
            .Where(module => IsKotlinModule(module, context.Modules))
            .OrderBy(module => module.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (modules.Length == 0
            && context.Modules.Count == 0
            && context.Language.Equals(Language, StringComparison.OrdinalIgnoreCase))
        {
            modules =
            [
                new ProjectModuleContext
                {
                    Name = context.ProjectName,
                    Path = context.RootPath,
                    Languages = [Language]
                }
            ];
        }

        if (modules.Length == 0
            && (context.Language.Equals(Language, StringComparison.OrdinalIgnoreCase)
                || context.Modules.Any(module => module.Languages.Any(language =>
                    language.Equals(Language, StringComparison.OrdinalIgnoreCase)))))
        {
            context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
            {
                Code = "KotlinModulesNotSelected",
                Component = nameof(KotlinSemanticAnalysisOrchestrator),
                Message = "Kotlin was detected, but no Kotlin module with an owned source scope could be selected for semantic analysis.",
                Target = context.RootPath,
                AffectsCoverage = true
            });
        }

        foreach (var module in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceRoots = _sourceRootResolver.ResolveSourceRoots(module, Language);
            if (sourceRoots.Count == 0)
            {
                context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "KotlinSourceRootsNotFound",
                    Component = nameof(KotlinSemanticAnalysisOrchestrator),
                    Message = "The Kotlin module has no existing Kotlin source roots; no source files could be analysed.",
                    Target = module.Path,
                    AffectsCoverage = true
                });
                continue;
            }

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
            ".kt",
            Language,
            context.DiscoveryDiagnostics);
        if (sourceFiles.Count == 0)
        {
            context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
            {
                Code = "KotlinSourceFilesNotFound",
                Component = nameof(KotlinSemanticAnalysisOrchestrator),
                Message = $"The {(includeTestCode ? "test" : "production")} source scope contains no Kotlin files.",
                Target = module.Path,
                AffectsCoverage = true
            });
            return;
        }

        var classpath = _classpathResolver.Resolve(module, context.Modules, includeTestCode);
        var (compilerVersion, languageVersion, apiVersion, jvmTarget, inferred) =
            ResolveCompilerSettings(module);
        var javaSourceRoots = _sourceRootResolver.ResolveReferencedSourceRoots(
                module, context, includeTestDependencies, "Java")
            .Concat(_sourceRootResolver.ResolveOwnSourceRoots(
                module, context, "Java", includeTestCode))
            .Distinct(PathComparer)
            .OrderBy(path => path, PathComparer)
            .ToArray();

        var request = new SemanticAnalysisRequest
        {
            Language = Language,
            ProjectPath = Path.GetFullPath(module.Path),
            WorkspaceRoot = Path.GetFullPath(context.RootPath),
            ModuleName = module.Name,
            BuildSystem = module.BuildSystem.ToString(),
            SourceFiles = sourceFiles,
            SourceRoots = sourceRoots.Select(root => Path.GetFullPath(root.Directory)).ToArray(),
            ReferencedSourceRoots = javaSourceRoots,
            TestSourceRoots = includeTestCode
                ? (module.IsTestProject || module.IsTestSupport
                    ? sourceRoots.Select(root => Path.GetFullPath(root.Directory)).ToArray()
                    : sourceRoots
                        .Where(root => root.Ownership == ProjectSourceRootOwnership.Test)
                        .Select(root => Path.GetFullPath(root.Directory))
                        .ToArray())
                : Array.Empty<string>(),
            GeneratedSourceRoots = sourceRoots
                .Where(root => root.IsGenerated)
                .Select(root => Path.GetFullPath(root.Directory))
                .ToArray(),
            ClasspathEntries = classpath.Entries
                .Concat(executionContext?.GetCompilationOutputs(module.Path, includeTestCode: false)
                    ?? Array.Empty<string>())
                .Distinct(PathComparer)
                .OrderBy(path => path, PathComparer)
                .ToArray(),
            UnresolvedClasspathDependencies = classpath.UnresolvedDependencies,
            ClasspathResolutionComplete = classpath.IsComplete,
            BuildFiles = module.ProjectFiles.ToArray(),
            RequestedCapabilities = ["symbols", "relationships", "compiler-diagnostics", "jvm-interop"],
            HasDeclaredDependencies = classpath.HasDeclaredDependencies,
            IncludeGeneratedCode = true,
            IncludeTestCode = includeTestCode,
            ProviderOptions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kotlin.compilerVersion"] = compilerVersion,
                ["kotlin.languageVersion"] = languageVersion,
                ["kotlin.apiVersion"] = apiVersion,
                ["kotlin.jvmTarget"] = jvmTarget,
                ["kotlin.moduleName"] = NormalizeModuleName(module.Name),
                ["kotlin.configurationInferred"] = inferred.ToString()
            }
        };

        var run = executionContext is null
            ? await _coordinator.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false)
            : await _coordinator.AnalyzeAsync(request, executionContext, cancellationToken).ConfigureAwait(false);
        context.SemanticAnalysisRuns.Add(run);

        _logger.LogInformation(
            "Kotlin {SourceSet} analysis for module {ModuleName} completed with status {Status}: {SymbolCount} symbols, {RelationshipCount} relationships, {DiagnosticCount} diagnostics.",
            includeTestCode ? "test" : "production",
            module.Name,
            run.Status,
            run.Symbols.Count,
            run.Relationships.Count,
            run.Diagnostics.Count);
    }

    private static bool IsKotlinModule(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectModuleContext> modules)
    {
        var hasOwnedKotlinRoots = module.SourceRoots.Any(root => root.ContainsKotlinSources)
            || Directory.Exists(Path.Combine(module.Path, "src", "main", "kotlin"))
            || Directory.Exists(Path.Combine(module.Path, "src", "test", "kotlin"));
        var hasKotlinLanguage = module.Languages.Any(language =>
            language.Equals(Language, StringComparison.OrdinalIgnoreCase));
        var ownsNestedModules = modules.Any(candidate =>
            !ReferenceEquals(candidate, module)
            && IsSameOrDescendantPath(Path.GetFullPath(candidate.Path), Path.GetFullPath(module.Path)));

        return hasOwnedKotlinRoots || (hasKotlinLanguage && !ownsNestedModules);
    }

    private static (string CompilerVersion, string LanguageVersion, string ApiVersion, string JvmTarget, bool Inferred)
        ResolveCompilerSettings(ProjectModuleContext module)
    {
        var gradle = module.GradleBuildModel;
        var maven = module.MavenModel;

        var compilerVersion = FirstNonBlank(gradle?.KotlinPluginVersion, maven?.KotlinPluginVersion);
        var languageVersion = FirstNonBlank(gradle?.KotlinLanguageVersion, maven?.KotlinLanguageVersion);
        var apiVersion = FirstNonBlank(gradle?.KotlinApiVersion, maven?.KotlinApiVersion);
        var jvmTarget = FirstNonBlank(
            gradle?.KotlinJvmTarget,
            maven?.KotlinJvmTarget,
            maven?.KotlinJdkRelease,
            gradle?.JavaToolchainVersion);

        var inferred = compilerVersion is null
            || languageVersion is null
            || apiVersion is null
            || jvmTarget is null;

        return (
            compilerVersion ?? "2.4.21",
            languageVersion ?? "2.4",
            apiVersion ?? "2.4",
            jvmTarget ?? "1.8",
            inferred);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string NormalizeModuleName(string value)
    {
        var normalized = new string(value
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
                ? character
                : '_')
            .ToArray());

        if (string.IsNullOrWhiteSpace(normalized))
            return "aegis_module";

        return char.IsAsciiLetter(normalized[0]) || normalized[0] == '_'
            ? normalized
            : $"aegis_{normalized}";
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
