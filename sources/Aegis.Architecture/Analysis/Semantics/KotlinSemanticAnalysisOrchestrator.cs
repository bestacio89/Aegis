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
    private readonly AndroidSemanticAnalysisPlanner _androidPlanner;
    private readonly KotlinMultiplatformSemanticAnalysisPlanner _multiplatformPlanner = new();

    private const string KotlinLanguage = "Kotlin";

    public string Language => KotlinLanguage;

    public int ExecutionOrder => 100;

    public KotlinSemanticAnalysisOrchestrator(
        IEnumerable<ISemanticAnalysisAdapter> adapters,
        IJvmClasspathResolver classpathResolver,
        ILogger<KotlinSemanticAnalysisOrchestrator> logger,
        SemanticAnalysisCoordinator? coordinator = null,
        JvmSourceRootResolver? sourceRootResolver = null,
        AndroidSemanticAnalysisPlanner? androidPlanner = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(classpathResolver);
        ArgumentNullException.ThrowIfNull(logger);

        _coordinator = coordinator ?? new SemanticAnalysisCoordinator(adapters);
        _classpathResolver = classpathResolver;
        _logger = logger;
        _sourceRootResolver = sourceRootResolver ?? new JvmSourceRootResolver();
        _androidPlanner = androidPlanner ?? new AndroidSemanticAnalysisPlanner(classpathResolver);
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
            .ToArray();

        if (modules.Length == 0
            && context.Modules.Count == 0
            && context.Language.Equals(KotlinLanguage, StringComparison.OrdinalIgnoreCase))
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
            && (context.Language.Equals(KotlinLanguage, StringComparison.OrdinalIgnoreCase)
                || context.Modules.Any(module => module.Languages.Any(language =>
                    language.Equals(KotlinLanguage, StringComparison.OrdinalIgnoreCase)))))
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

        var sourceRootsByModule = new Dictionary<string, IReadOnlyList<ProjectSourceRoot>>(PathComparer);
        var androidScopesByModule = new Dictionary<string, IReadOnlyList<AndroidSemanticAnalysisPlanner.Scope>>(PathComparer);
        var unsupportedModulePaths = new HashSet<string>(PathComparer);
        var multiplatformScopesByModule =
            new Dictionary<string, KotlinMultiplatformSemanticAnalysisPlanner.TargetScope>(PathComparer);

        foreach (var module in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetUnsupportedTarget(module, out var unsupportedTarget))
            {
                context.SemanticAnalysisRuns.Add(CreateUnsupportedTargetRun(module, unsupportedTarget));
                unsupportedModulePaths.Add(NormalizeModulePath(module.Path));
                continue;
            }

            if (module.GradleBuildModel?.KotlinMultiplatform is not null)
            {
                var moduleKey = NormalizeModulePath(module.Path);
                var allSourceRoots = _sourceRootResolver.ResolveSourceRoots(module, Language);
                var plan = _multiplatformPlanner.Resolve(module, allSourceRoots);
                context.DiscoveryDiagnostics.AddRange(plan.Diagnostics);

                foreach (var unsupportedScope in plan.Targets.Where(scope => !scope.IsSupported))
                {
                    var diagnostic = unsupportedScope.Diagnostic!;
                    context.SemanticAnalysisRuns.Add(CreateUnsupportedTargetRun(
                        module,
                        (diagnostic.Code, diagnostic.Message),
                        unsupportedScope.TargetName,
                        unsupportedScope.ProductionSourceSetNames.Concat(unsupportedScope.TestSourceSetNames).ToArray()));
                }

                var supportedScopes = plan.Targets.Where(scope => scope.IsSupported).ToArray();
                if (supportedScopes.Length != 1)
                {
                    if (supportedScopes.Length > 1)
                    {
                        context.SemanticAnalysisRuns.Add(CreateUnsupportedTargetRun(
                            module,
                            ("KotlinMultiplatformJvmTargetAmbiguous",
                                "More than one JVM target was resolved for this module. Aegis requires an unambiguous target compilation scope."),
                            null));
                    }

                    unsupportedModulePaths.Add(moduleKey);
                    if (plan.Targets.Count == 0)
                    {
                        context.SemanticAnalysisRuns.Add(CreateUnsupportedTargetRun(
                            module,
                            ("KotlinMultiplatformTargetsNotResolved",
                                "The Kotlin Multiplatform model contains no statically resolved compilation targets.")));
                    }
                    continue;
                }

                var scope = supportedScopes[0];
                multiplatformScopesByModule[moduleKey] = scope;
                sourceRootsByModule[moduleKey] = scope.ProductionSourceRoots
                    .Concat(scope.TestSourceRoots)
                    .DistinctBy(root => root.Directory, PathComparer)
                    .ToArray();
                continue;
            }

            if (module.GradleBuildModel?.Android is not null)
            {
                var androidScopes = _androidPlanner.ResolveScopes(module, context, Language, executionContext);
                var moduleKey = NormalizeModulePath(module.Path);
                androidScopesByModule[moduleKey] = androidScopes;
                sourceRootsByModule[moduleKey] = androidScopes
                    .SelectMany(scope => scope.SourceRoots)
                    .DistinctBy(root => root.Directory, PathComparer)
                    .ToArray();
                continue;
            }

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

            sourceRootsByModule[NormalizeModulePath(module.Path)] = sourceRoots;
        }

        var productionModules = modules
            .Where(module =>
                !unsupportedModulePaths.Contains(NormalizeModulePath(module.Path))
                && sourceRootsByModule.TryGetValue(NormalizeModulePath(module.Path), out var roots)
                && !module.IsTestProject
                && !module.IsTestSupport
                && roots.Any(root => root.Ownership == ProjectSourceRootOwnership.Production)
                && roots.All(root => root.Ownership != ProjectSourceRootOwnership.Unknown))
            .ToArray();
        var productionOrder = JvmModuleDependencyResolver.OrderModules(
            productionModules,
            context.Modules,
            context.Dependencies,
            includeTestDependencies: false,
            out var blockedProductionModules);
        AddDependencyOrderDiagnostics(context, blockedProductionModules, includeTestDependencies: false);

        foreach (var module in productionOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var moduleKey = NormalizeModulePath(module.Path);
            var roots = sourceRootsByModule[moduleKey];
            if (multiplatformScopesByModule.TryGetValue(moduleKey, out var multiplatformScope))
            {
                if (multiplatformScope.ProductionSourceRoots.Count > 0)
                {
                    await AnalyzeScopeAsync(
                        module,
                        context,
                        multiplatformScope.ProductionSourceRoots,
                        includeTestCode: false,
                        includeTestDependencies: false,
                        executionContext,
                        cancellationToken,
                        targetName: multiplatformScope.TargetName,
                        sourceSetNames: multiplatformScope.ProductionSourceSetNames,
                        sharedSourceRoots: multiplatformScope.ProductionSharedSourceRoots
                            .Select(root => root.Directory).ToArray(),
                        selectedGradleDependencies: SelectMultiplatformDependencies(
                            module, multiplatformScope.ProductionSourceSetNames, includeTestCode: false))
                        .ConfigureAwait(false);
                }
                continue;
            }

            if (androidScopesByModule.TryGetValue(moduleKey, out var androidScopes))
            {
                foreach (var androidScope in androidScopes.Where(scope => !scope.IncludeTestCode))
                {
                    var resolvedScope = _androidPlanner.ResolveInputs(
                        module, context, androidScope, executionContext);
                    await AnalyzeScopeAsync(
                        module, context, resolvedScope.SourceRoots, false, false,
                        executionContext, cancellationToken, resolvedScope).ConfigureAwait(false);
                }
                continue;
            }

            var productionRoots = roots
                .Where(root => root.Ownership != ProjectSourceRootOwnership.Test)
                .ToArray();
            if (productionRoots.Length == 0)
                continue;

            await AnalyzeScopeAsync(
                module,
                context,
                productionRoots,
                includeTestCode: false,
                includeTestDependencies: false,
                executionContext,
                cancellationToken).ConfigureAwait(false);
        }

        var testModules = modules
            .Where(module =>
                !unsupportedModulePaths.Contains(NormalizeModulePath(module.Path))
                && sourceRootsByModule.TryGetValue(NormalizeModulePath(module.Path), out var roots)
                && (module.IsTestProject
                    || module.IsTestSupport
                    || roots.Any(root => root.Ownership is ProjectSourceRootOwnership.Test or ProjectSourceRootOwnership.Unknown)))
            .ToArray();
        var testOrder = JvmModuleDependencyResolver.OrderModules(
            testModules,
            context.Modules,
            context.Dependencies,
            includeTestDependencies: true,
            out var blockedTestModules);
        AddDependencyOrderDiagnostics(context, blockedTestModules, includeTestDependencies: true);

        foreach (var module in testOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var moduleKey = NormalizeModulePath(module.Path);
            var roots = sourceRootsByModule[moduleKey];
            if (multiplatformScopesByModule.TryGetValue(moduleKey, out var multiplatformScope))
            {
                if (multiplatformScope.TestSourceRoots.Count > 0)
                {
                    await AnalyzeScopeAsync(
                        module,
                        context,
                        multiplatformScope.TestSourceRoots,
                        includeTestCode: true,
                        includeTestDependencies: true,
                        executionContext,
                        cancellationToken,
                        targetName: multiplatformScope.TargetName,
                        sourceSetNames: multiplatformScope.TestSourceSetNames,
                        sharedSourceRoots: multiplatformScope.TestSharedSourceRoots
                            .Select(root => root.Directory).ToArray(),
                        selectedGradleDependencies: SelectMultiplatformDependencies(
                            module,
                            multiplatformScope.ProductionSourceSetNames
                                .Concat(multiplatformScope.TestSourceSetNames).Distinct(StringComparer.Ordinal).ToArray(),
                            includeTestCode: true))
                        .ConfigureAwait(false);
                }
                continue;
            }

            if (androidScopesByModule.TryGetValue(moduleKey, out var androidScopes))
            {
                foreach (var androidScope in androidScopes.Where(scope => scope.IncludeTestCode))
                {
                    var resolvedScope = _androidPlanner.ResolveInputs(
                        module, context, androidScope, executionContext);
                    await AnalyzeScopeAsync(
                        module, context, resolvedScope.SourceRoots, true, true,
                        executionContext, cancellationToken, resolvedScope).ConfigureAwait(false);
                }
                continue;
            }

            if (roots.Any(root => root.Ownership == ProjectSourceRootOwnership.Unknown))
            {
                await AnalyzeScopeAsync(
                    module,
                    context,
                    roots,
                    includeTestCode: true,
                    includeTestDependencies: module.IsTestProject || module.IsTestSupport,
                    executionContext,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            var testRoots = module.IsTestProject || module.IsTestSupport
                ? roots
                : roots.Where(root => root.Ownership == ProjectSourceRootOwnership.Test).ToArray();
            if (testRoots.Count == 0)
                continue;

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
        ModuleRoleDetector.Analyze(context.Modules);
    }

    private static void AddDependencyOrderDiagnostics(
        ProjectArchitectureContext context,
        IReadOnlyCollection<string> blockedModulePaths,
        bool includeTestDependencies)
    {
        foreach (var modulePath in blockedModulePaths)
        {
            context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
            {
                Code = includeTestDependencies
                    ? "KotlinTestDependencyOrderBlocked"
                    : "KotlinProductionDependencyOrderBlocked",
                Component = nameof(KotlinSemanticAnalysisOrchestrator),
                Message = "The explicit JVM project-dependency graph contains a cycle or an ambiguous module path. This scope was skipped because a dependency-first compilation order could not be established.",
                Target = modulePath,
                AffectsCoverage = true
            });
        }
    }

    private static IReadOnlyCollection<GradleBuildModel.Dependency> SelectMultiplatformDependencies(
        ProjectModuleContext module,
        IReadOnlyCollection<string> sourceSetNames,
        bool includeTestCode)
    {
        var model = module.GradleBuildModel;
        if (model is null || model.KotlinMultiplatform is null)
            return Array.Empty<GradleBuildModel.Dependency>();

        var selectedSourceSets = sourceSetNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allSourceSets = model.KotlinMultiplatform.SourceSets
            .Select(sourceSet => sourceSet.Name)
            .ToArray();

        return model.Dependencies.Where(dependency =>
        {
            var configuration = dependency.Configuration;
            var owningSourceSet = allSourceSets
                .Where(name => configuration.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(name => name.Length)
                .FirstOrDefault();
            if (owningSourceSet is not null)
                return selectedSourceSets.Contains(owningSourceSet);

            var normalized = configuration.Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
            if (!includeTestCode && normalized.StartsWith("test", StringComparison.Ordinal))
                return false;

            return normalized is "api" or "implementation" or "compileonly" or "compile" or "provided"
                || (includeTestCode
                    && normalized is "testapi" or "testimplementation" or "testcompileonly" or "testcompile");
        }).ToArray();
    }

    private static string NormalizeModulePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private async Task AnalyzeScopeAsync(
        ProjectModuleContext module,
        ProjectArchitectureContext context,
        IReadOnlyCollection<ProjectSourceRoot> sourceRoots,
        bool includeTestCode,
        bool includeTestDependencies,
        SemanticAnalysisExecutionContext? executionContext,
        CancellationToken cancellationToken,
        AndroidSemanticAnalysisPlanner.Scope? androidScope = null,
        string? targetName = null,
        IReadOnlyCollection<string>? sourceSetNames = null,
        IReadOnlyCollection<string>? sharedSourceRoots = null,
        IReadOnlyCollection<GradleBuildModel.Dependency>? selectedGradleDependencies = null)
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

        var classpath = androidScope is not null
            ? new JvmClasspathResolution(androidScope.ClasspathEntries, androidScope.UnresolvedDependencies,
                androidScope.HasDeclaredDependencies, androidScope.IsComplete)
            : selectedGradleDependencies is not null
                ? _classpathResolver.Resolve(module, context.Modules, selectedGradleDependencies)
                : _classpathResolver.Resolve(module, context.Modules, includeTestCode);
        var (compilerVersion, languageVersion, apiVersion, jvmTarget, inferred) =
            ResolveCompilerSettings(module);
        var javaSourceRoots = _sourceRootResolver.ResolveReferencedSourceRoots(
                module, context, includeTestDependencies, "Java")
            .Concat(_sourceRootResolver.ResolveOwnSourceRoots(
                module, context, "Java", includeTestCode))
            .Distinct(PathComparer)
            .OrderBy(path => path, PathComparer)
            .ToArray();

        var dependencyPaths = JvmModuleDependencyResolver.ResolveDependencyPaths(
            module.Path,
            context.Modules,
            context.Dependencies,
            includeTestDependencies);
        var compilationOutputs = androidScope is null
            ? executionContext?.GetCompilationOutputs(module.Path, dependencyPaths.Production, dependencyPaths.Test, includeTestCode)
                ?? Array.Empty<string>()
            : Array.Empty<string>();

        var composePlugin = module.GradleBuildModel?.Plugins.FirstOrDefault(plugin =>
            plugin.IsApplied
            && plugin.Id.Equals("org.jetbrains.kotlin.plugin.compose", StringComparison.Ordinal));
        var composePluginEnabled = composePlugin?.Version is { } composePluginVersion
            && composePluginVersion.Equals(compilerVersion, StringComparison.OrdinalIgnoreCase);
        var composeDependencyDeclared = module.GradleBuildModel?.Dependencies.Any(dependency =>
            dependency.Group?.StartsWith("androidx.compose", StringComparison.OrdinalIgnoreCase) == true
            || dependency.Group?.Equals("androidx.activity", StringComparison.OrdinalIgnoreCase) == true
                && dependency.Artifact?.Equals("activity-compose", StringComparison.OrdinalIgnoreCase) == true) == true;
        var composePluginNotExecuted = composeDependencyDeclared && !composePluginEnabled
            || composePlugin is not null && !composePluginEnabled;
        if (composePlugin is not null && !composePluginEnabled)
        {
            context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
            {
                Code = "KotlinComposeCompilerPluginVersionMismatch",
                Component = nameof(KotlinSemanticAnalysisOrchestrator),
                Message = "The applied Compose compiler plugin version is missing or does not match the Kotlin compiler version selected for this analysis. Compose compiler transformations were not executed.",
                Target = module.Path,
                AffectsCoverage = true
            });
        }
        else if (composeDependencyDeclared && !composePluginEnabled)
        {
            context.DiscoveryDiagnostics.Add(new AnalysisDiagnostic
            {
                Code = "KotlinComposeCompilerPluginNotApplied",
                Component = nameof(KotlinSemanticAnalysisOrchestrator),
                Message = "Compose dependencies are declared, but the module does not apply the Kotlin Compose compiler plugin. Composable compiler transformations and validation are unavailable.",
                Target = module.Path,
                AffectsCoverage = true
            });
        }

        var compilerPluginsNotExecuted = composePluginNotExecuted
            || module.GradleBuildModel?.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "GradleKotlinCompilerPluginNotExecuted") == true
            || module.MavenModel?.Diagnostics.Any(diagnostic =>
                diagnostic.Code is "MavenKotlinCompilerPluginNotExecuted" or "MavenKotlinCodeGenerationNotExecuted") == true;

        var request = new SemanticAnalysisRequest
        {
            Language = Language,
            ProjectPath = Path.GetFullPath(module.Path),
            WorkspaceRoot = Path.GetFullPath(context.RootPath),
            ModuleName = module.Name,
            BuildSystem = module.BuildSystem.ToString(),
            SourceFiles = sourceFiles,
            SourceRoots = sourceRoots.Select(root => Path.GetFullPath(root.Directory)).ToArray(),
            SharedSourceRoots = sharedSourceRoots?.Select(Path.GetFullPath).ToArray() ?? Array.Empty<string>(),
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
            // Classpath order is semantically significant when binary names
            // collide. Preserve resolver order, then append ordered project outputs.
            ClasspathEntries = classpath.Entries
                .Concat(compilationOutputs)
                .Distinct(PathComparer)
                .ToArray(),
            UnresolvedClasspathDependencies = classpath.UnresolvedDependencies,
            ClasspathResolutionComplete = classpath.IsComplete,
            InputDiagnostics = androidScope?.Diagnostics ?? Array.Empty<AnalysisDiagnostic>(),
            BuildFiles = module.ProjectFiles.ToArray(),
            TargetName = targetName,
            SourceSetNames = sourceSetNames?.ToArray() ?? Array.Empty<string>(),
            RequestedCapabilities = targetName is null
                ? ["symbols", "relationships", "compiler-diagnostics", "jvm-interop"]
                : ["symbols", "relationships", "compiler-diagnostics", "jvm-interop", "multiplatform"],
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
                ["kotlin.target"] = targetName ?? string.Empty,
                ["kotlin.configurationInferred"] = inferred.ToString(),
                ["kotlin.compilerPluginsNotExecuted"] = compilerPluginsNotExecuted.ToString(),
                ["kotlin.composeCompilerPluginEnabled"] = composePluginEnabled.ToString(),
                ["android.variantName"] = androidScope?.VariantName ?? string.Empty,
                ["android.sourceScope"] = androidScope?.DependencyScope.ToString() ?? string.Empty,
                ["android.compileSdk"] = module.GradleBuildModel?.Android?.CompileSdk ?? string.Empty
            }
        };

        var run = executionContext is null
            ? await _coordinator.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false)
            : await _coordinator.AnalyzeAsync(request, executionContext, cancellationToken).ConfigureAwait(false);
        if (targetName is not null)
            run = WithTargetMetadata(run, targetName, sourceSetNames);

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


    private static SemanticAnalysisRun WithTargetMetadata(
        SemanticAnalysisRun run,
        string targetName,
        IReadOnlyCollection<string>? sourceSetNames) =>
        new()
        {
            RunId = run.RunId,
            Language = run.Language,
            EngineId = run.EngineId,
            EngineVersion = run.EngineVersion,
            ProjectPath = run.ProjectPath,
            TargetName = targetName,
            SourceSetNames = sourceSetNames?.ToList() ?? [],
            Status = run.Status,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            AnalyzedFileCount = run.AnalyzedFileCount,
            SkippedFileCount = run.SkippedFileCount,
            Capabilities = run.Capabilities,
            Symbols = run.Symbols,
            Relationships = run.Relationships,
            Diagnostics = run.Diagnostics
        };

    private static bool TryGetUnsupportedTarget(
        ProjectModuleContext module,
        out (string Code, string Message) unsupportedTarget)
    {
        var plugins = module.GradleBuildModel?.Plugins ?? [];
        var androidModel = module.GradleBuildModel?.Android;
        if (androidModel is not null
            && androidModel.KotlinIntegration is "Disabled" or "NotDeclared" or "Unknown")
        {
            unsupportedTarget = (
                "KotlinAndroidIntegrationNotResolved",
                "Kotlin sources were discovered in an Android module, but the active Android/Kotlin compiler integration is not statically established.");
            return true;
        }
        var androidPluginWithoutModel = module.GradleBuildModel?.Android is null
            && module.GradleBuildModel?.KotlinMultiplatform is null
            ? plugins.FirstOrDefault(plugin => plugin.IsApplied
                && (plugin.Id.StartsWith("com.android.", StringComparison.Ordinal)
                    || plugin.Id is "org.jetbrains.kotlin.android" or "kotlin-android"))
            : null;
        if (androidPluginWithoutModel is not null)
        {
            unsupportedTarget = ("AndroidVariantModelUnavailable",
                "An Android Gradle plugin is applied, but its static Android variant model is unavailable. The module cannot be analysed safely as a plain JVM project.");
            return true;
        }
        var unsupportedPlugin = plugins.FirstOrDefault(plugin =>
            plugin.IsApplied
            && ((plugin.Id.Equals("org.jetbrains.kotlin.multiplatform", StringComparison.Ordinal)
                    && module.GradleBuildModel?.KotlinMultiplatform is null)
            || plugin.Id.Equals("org.jetbrains.kotlin.js", StringComparison.Ordinal)
            || plugin.Id.StartsWith("org.jetbrains.kotlin.native", StringComparison.Ordinal)
            || plugin.Id.StartsWith("org.jetbrains.kotlin.wasm", StringComparison.Ordinal)));

        if (unsupportedPlugin is null)
        {
            var kotlinJvmPluginMissing =
                module.GradleBuildModel?.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == "GradleKotlinJvmPluginNotDetected") == true
                || module.MavenModel?.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == "MavenKotlinPluginNotDetected") == true;
            if (kotlinJvmPluginMissing)
            {
                unsupportedTarget = (
                    "KotlinJvmPluginNotDetected",
                    "Kotlin sources were discovered, but no statically resolved Kotlin/JVM compiler plugin was found. Aegis will not infer a JVM target from source syntax alone.");
                return true;
            }

            unsupportedTarget = (string.Empty, string.Empty);
            return false;
        }

        var message = unsupportedPlugin.Id.Equals("org.jetbrains.kotlin.multiplatform", StringComparison.Ordinal)
            ? "Kotlin Multiplatform is not analysed as a single JVM module. Target-specific source sets, expect/actual relationships and per-target classpaths must be modelled first."
            : "This Kotlin target is not JVM bytecode. A target-specific semantic engine and dependency model are required.";

        unsupportedTarget = ("KotlinTargetPlatformNotSupported", message);
        return true;
    }

    private static SemanticAnalysisRun CreateUnsupportedTargetRun(
        ProjectModuleContext module,
        (string Code, string Message) unsupportedTarget,
        string? targetName = null,
        IReadOnlyCollection<string>? sourceSetNames = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new SemanticAnalysisRun
        {
            RunId = Guid.NewGuid().ToString("N"),
            Language = KotlinLanguage,
            EngineId = nameof(KotlinSemanticAnalysisOrchestrator),
            ProjectPath = module.Path,
            TargetName = targetName,
            SourceSetNames = sourceSetNames?.ToList() ?? [],
            Status = SemanticAnalysisStatus.Unsupported,
            StartedAtUtc = now,
            CompletedAtUtc = now,
            Diagnostics =
            [
                new AnalysisDiagnostic
                {
                    Code = unsupportedTarget.Code,
                    Component = nameof(KotlinSemanticAnalysisOrchestrator),
                    Message = unsupportedTarget.Message,
                    Target = module.Path,
                    AffectsCoverage = true
                }
            ]
        };
    }

    private static bool IsKotlinModule(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectModuleContext> modules)
    {
        var hasOwnedKotlinRoots = module.SourceRoots.Any(root => root.ContainsKotlinSources)
            || module.GradleBuildModel?.Android?.SourceRoots.Any(root => root.ContainsKotlinSources) == true
            || Directory.Exists(Path.Combine(module.Path, "src", "main", "kotlin"))
            || Directory.Exists(Path.Combine(module.Path, "src", "test", "kotlin"));
        var hasKotlinLanguage = module.Languages.Any(language =>
            language.Equals(KotlinLanguage, StringComparison.OrdinalIgnoreCase));
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
        var declaredJvmTarget = FirstNonBlank(
            gradle?.KotlinJvmTarget,
            AndroidSemanticAnalysisPlanner.ResolveAndroidJvmTarget(module, preferSourceCompatibility: false),
            maven?.KotlinJvmTarget,
            maven?.KotlinJdkRelease);
        var jvmTarget = declaredJvmTarget ?? FirstNonBlank(gradle?.JavaToolchainVersion);

        var inferred = gradle?.KotlinCompilerConfigurationInferred == true
            || maven?.KotlinCompilerConfigurationInferred == true
            || compilerVersion is null
            || languageVersion is null
            || apiVersion is null
            || declaredJvmTarget is null;

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
