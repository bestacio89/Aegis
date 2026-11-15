using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Builds variant- and source-scope-specific Java/Kotlin inputs from the
/// statically discovered Android model and host-validated local artifacts.
/// </summary>
public sealed class AndroidSemanticAnalysisPlanner
{
    private readonly AndroidSdkResolver _sdkResolver;
    private readonly AndroidVariantDependencySelector _dependencySelector;
    private readonly AndroidVariantProjectDependencyResolver _projectDependencyResolver;
    private readonly IJvmClasspathResolver _classpathResolver;

    public AndroidSemanticAnalysisPlanner(
        IJvmClasspathResolver classpathResolver,
        AndroidSdkResolver? sdkResolver = null,
        AndroidVariantDependencySelector? dependencySelector = null,
        AndroidVariantProjectDependencyResolver? projectDependencyResolver = null)
    {
        ArgumentNullException.ThrowIfNull(classpathResolver);
        _classpathResolver = classpathResolver;
        _sdkResolver = sdkResolver ?? new AndroidSdkResolver();
        _dependencySelector = dependencySelector ?? new AndroidVariantDependencySelector();
        _projectDependencyResolver = projectDependencyResolver ?? new AndroidVariantProjectDependencyResolver();
    }

    public IReadOnlyList<Scope> ResolveScopes(
        ProjectModuleContext module,
        ProjectArchitectureContext context,
        string language,
        SemanticAnalysisExecutionContext? executionContext)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var android = module.GradleBuildModel?.Android;
        if (android is null)
            return [];

        var diagnostics = new List<AnalysisDiagnostic>();
        if (android.Variants.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                "AndroidVariantModelUnavailable",
                "The Android module has no statically derived variants; semantic execution cannot select a safe variant.",
                module.Path));
            context.DiscoveryDiagnostics.AddRange(diagnostics);
            return [];
        }

        if (!android.IsComplete)
        {
            diagnostics.Add(Diagnostic(
                "AndroidVariantModelIncomplete",
                "The static Android model is incomplete. Known variant scopes may be analysed, but their semantic results remain partial.",
                module.Path));
        }

        var scopes = new List<Scope>();
        foreach (var group in android.Variants.GroupBy(variant => variant.Name, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1)
            {
                diagnostics.Add(Diagnostic(
                    "AndroidVariantIdentityAmbiguous",
                    "A blank or duplicate Android variant identity prevents safe source and classpath selection.",
                    module.Path));
                continue;
            }

            var variantName = group.Key;
            var productionRoots = ResolveSourceRoots(module, language, variantName, GradleAndroidTestKind.None, diagnostics);
            if (productionRoots.Count > 0)
                scopes.Add(CreateSourceScope(variantName, AndroidVariantDependencyScope.Production,
                    productionRoots, includeTestCode: false));

            var unitTestRoots = ResolveSourceRoots(module, language, variantName, GradleAndroidTestKind.Unit, diagnostics);
            if (unitTestRoots.Count > 0)
                scopes.Add(CreateSourceScope(variantName, AndroidVariantDependencyScope.UnitTest,
                    unitTestRoots, includeTestCode: true));
        }

        foreach (var sourceSet in android.SourceSets.Where(sourceSet =>
                     sourceSet.TestKind is GradleAndroidTestKind.Instrumentation or GradleAndroidTestKind.Fixtures))
        {
            if (GetDirectories(sourceSet, language).Count == 0)
                continue;
            diagnostics.Add(Diagnostic(
                sourceSet.TestKind == GradleAndroidTestKind.Instrumentation
                    ? "AndroidInstrumentationTestSemanticScopeNotEnabled"
                    : "AndroidTestFixturesSemanticScopeNotEnabled",
                $"The {sourceSet.TestKind} source set '{sourceSet.Name}' is discovered but is not part of the Java/Kotlin worker's supported production/unit-test scope.",
                module.Path));
        }

        if (scopes.Count == 0 && diagnostics.Count == 0)
            diagnostics.Add(Diagnostic(
                "AndroidSourceRootsUnavailable",
                $"No variant-owned {language} production or unit-test source roots were discovered for this Android module.",
                module.Path));

        if (diagnostics.Count > 0)
        {
            context.DiscoveryDiagnostics.AddRange(diagnostics);
            var sourceDiagnostics = diagnostics.ToArray();
            scopes = scopes.Select(scope => scope with
            {
                Diagnostics = sourceDiagnostics,
                IsComplete = sourceDiagnostics.All(diagnostic => !diagnostic.AffectsCoverage)
            }).ToList();
        }
        return scopes;
    }

    public Scope ResolveInputs(
        ProjectModuleContext module,
        ProjectArchitectureContext context,
        Scope sourceScope,
        SemanticAnalysisExecutionContext? executionContext)
    {
        ArgumentNullException.ThrowIfNull(sourceScope);
        var resolved = ResolveScope(
            module,
            context,
            sourceScope.VariantName,
            sourceScope.DependencyScope,
            sourceScope.SourceRoots,
            sourceScope.IncludeTestCode,
            executionContext);
        var diagnostics = sourceScope.Diagnostics.Concat(resolved.Diagnostics).ToArray();
        return resolved with
        {
            Diagnostics = diagnostics,
            IsComplete = resolved.IsComplete && diagnostics.All(diagnostic => !diagnostic.AffectsCoverage)
        };
    }

    private static Scope CreateSourceScope(
        string variantName,
        AndroidVariantDependencyScope dependencyScope,
        IReadOnlyList<ProjectSourceRoot> sourceRoots,
        bool includeTestCode) =>
        new(
            variantName,
            dependencyScope,
            sourceRoots,
            [],
            [],
            false,
            false,
            [],
            includeTestCode,
            null);

    private Scope ResolveScope(
        ProjectModuleContext module,
        ProjectArchitectureContext context,
        string variantName,
        AndroidVariantDependencyScope dependencyScope,
        IReadOnlyList<ProjectSourceRoot> sourceRoots,
        bool includeTestCode,
        SemanticAnalysisExecutionContext? executionContext)
    {
        var diagnostics = new List<AnalysisDiagnostic>();
        var sdk = _sdkResolver.Resolve(context.RootPath, module.Path, module.GradleBuildModel!.Android!);
        diagnostics.AddRange(sdk.Diagnostics);

        var selection = _dependencySelector.Select(module, variantName, dependencyScope);
        diagnostics.AddRange(selection.Diagnostics);
        var externalClasspath = _classpathResolver.Resolve(module, context.Modules, selection.Dependencies);
        var projectDependencies = selection.Dependencies.Any(dependency =>
                !string.IsNullOrWhiteSpace(dependency.ProjectPath))
            ? _projectDependencyResolver.Resolve(module, context.Modules, selection)
            : new AndroidVariantProjectDependencyResolver.Result([], [], [], [], [], [], true);
        diagnostics.AddRange(projectDependencies.Diagnostics);

        var projectOutputs = new AndroidVariantProjectDependencyResolver.ClasspathResult([], [], true);
        if (executionContext is not null)
        {
            // This also supplies the consumer's own successful production output
            // to its test scopes and the Kotlin output to mixed Java scopes.
            projectOutputs = _projectDependencyResolver.AssembleCompilationOutputs(
                module, projectDependencies, executionContext, includeTestCode, variantName);
            diagnostics.AddRange(projectOutputs.Diagnostics);
        }
        else if (projectDependencies.OrderedDependencies.Count > 0)
        {
            diagnostics.Add(Diagnostic(
                "AndroidProjectCompilationOutputsUnavailable",
                "Project dependencies were declared, but no semantic execution context is available to supply host-validated compiler outputs.",
                module.Path));
        }

        var entries = new List<string>();
        if (sdk.AndroidJar is not null)
            entries.Add(sdk.AndroidJar);
        entries.AddRange(externalClasspath.Entries);
        entries.AddRange(projectOutputs.Entries);

        var hasDeclaredDependencies = externalClasspath.HasDeclaredDependencies || selection.Dependencies.Count > 0;
        var complete = sdk.IsComplete
            && selection.IsComplete
            && externalClasspath.IsComplete
            && projectDependencies.IsComplete
            && projectOutputs.IsComplete
            && diagnostics.All(diagnostic => !diagnostic.AffectsCoverage);

        return new Scope(
            variantName,
            dependencyScope,
            sourceRoots,
            entries.Distinct(PathComparer).ToArray(),
            externalClasspath.UnresolvedDependencies,
            hasDeclaredDependencies,
            complete,
            diagnostics,
            includeTestCode,
            sdk.AndroidJar);
    }

    private static IReadOnlyList<ProjectSourceRoot> ResolveSourceRoots(
        ProjectModuleContext module,
        string language,
        string variantName,
        GradleAndroidTestKind testKind,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var android = module.GradleBuildModel?.Android;
        if (android is null)
            return [];

        var roots = new Dictionary<string, ProjectSourceRoot>(PathComparer);
        foreach (var sourceSet in android.SourceSets.Where(sourceSet =>
                     sourceSet.AppliesToVariants.Contains(variantName, StringComparer.Ordinal)
                     && (testKind == GradleAndroidTestKind.None
                         ? sourceSet.TestKind == GradleAndroidTestKind.None
                         : sourceSet.TestKind == testKind)))
        {
            if (sourceSet.Ownership == ProjectSourceRootOwnership.Unknown)
            {
                diagnostics.Add(Diagnostic(
                    "AndroidSourceSetOwnershipUnknown",
                    $"Android source set '{sourceSet.Name}' has unknown production/test ownership and was excluded from semantic execution.",
                    module.Path));
                continue;
            }

            foreach (var directory in GetDirectories(sourceSet, language))
            {
                var normalized = Path.GetFullPath(directory);
                var generated = IsGeneratedDirectory(sourceSet, language, directory);
                if (!Directory.Exists(normalized))
                {
                    if (generated)
                        diagnostics.Add(Diagnostic(
                            "AndroidGeneratedSourceUnavailable",
                            $"Generated {language} source directory for Android source set '{sourceSet.Name}' is unavailable; generated declarations may be missing.",
                            normalized));
                    continue;
                }

                roots[normalized] = new ProjectSourceRoot
                {
                    Directory = normalized,
                    Ownership = sourceSet.Ownership,
                    Origin = ProjectSourceRootOrigin.Observed,
                    IsGenerated = generated,
                    Languages = [language],
                    ContainsJavaSources = language.Equals("Java", StringComparison.OrdinalIgnoreCase),
                    EvidenceFile = sourceSet.EvidenceFile
                };
            }
        }

        return roots.Values.OrderBy(root => root.Directory, PathComparer).ToArray();
    }

    private static IReadOnlyList<string> GetDirectories(GradleAndroidSourceSet sourceSet, string language)
    {
        var isJava = language.Equals("Java", StringComparison.OrdinalIgnoreCase);
        var values = isJava
            ? sourceSet.JavaSourceDirectories.Concat(sourceSet.GeneratedJavaSourceDirectories)
            : sourceSet.KotlinSourceDirectories.Concat(sourceSet.GeneratedKotlinSourceDirectories);
        return values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(PathComparer)
            .ToArray();
    }

    private static bool IsGeneratedDirectory(GradleAndroidSourceSet sourceSet, string language, string directory) =>
        (language.Equals("Java", StringComparison.OrdinalIgnoreCase)
            ? sourceSet.GeneratedJavaSourceDirectories
            : sourceSet.GeneratedKotlinSourceDirectories)
        .Contains(directory, PathComparer);

    public static string? ResolveAndroidJvmTarget(
        ProjectModuleContext module,
        bool preferSourceCompatibility)
    {
        ArgumentNullException.ThrowIfNull(module);
        var options = module.GradleBuildModel?.Android?.CompileOptions;
        var value = preferSourceCompatibility
            ? options?.SourceCompatibility ?? options?.TargetCompatibility
            : options?.TargetCompatibility ?? options?.SourceCompatibility;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        const string javaVersionPrefix = "JavaVersion.VERSION_";
        var normalized = value.Trim();
        if (normalized.StartsWith(javaVersionPrefix, StringComparison.Ordinal))
            normalized = normalized[javaVersionPrefix.Length..].Replace('_', '.');

        return normalized;
    }

    private static AnalysisDiagnostic Diagnostic(string code, string message, string target) =>
        new()
        {
            Code = code,
            Component = nameof(AndroidSemanticAnalysisPlanner),
            Message = message,
            Target = target,
            AffectsCoverage = true
        };

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public sealed record Scope(
        string VariantName,
        AndroidVariantDependencyScope DependencyScope,
        IReadOnlyList<ProjectSourceRoot> SourceRoots,
        IReadOnlyList<string> ClasspathEntries,
        IReadOnlyList<string> UnresolvedDependencies,
        bool HasDeclaredDependencies,
        bool IsComplete,
        IReadOnlyList<AnalysisDiagnostic> Diagnostics,
        bool IncludeTestCode,
        string? AndroidJar);
}
