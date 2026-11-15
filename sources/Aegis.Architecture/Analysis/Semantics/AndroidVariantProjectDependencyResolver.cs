using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Resolves statically selected Android project dependencies to modules in the
/// consumer's owning Gradle build. Project paths are never resolved globally.
/// </summary>
public sealed class AndroidVariantProjectDependencyResolver
{
    public Result Resolve(
        ProjectModuleContext consumer,
        IReadOnlyCollection<ProjectModuleContext> modules,
        AndroidVariantDependencySelection selection)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer.Path);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(selection);

        var diagnostics = selection.Diagnostics.ToList();
        var complete = selection.IsComplete;
        var owningBuilds = modules
            .Where(module => module.GradleModel is not null
                && module.GradleModel.Projects.Any(project =>
                    PathsEqual(project.ProjectDirectory, consumer.Path)))
            .ToArray();

        if (owningBuilds.Length != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                owningBuilds.Length == 0
                    ? "AndroidProjectDependencyOwningBuildUnavailable"
                    : "AndroidProjectDependencyOwningBuildAmbiguous",
                owningBuilds.Length == 0
                    ? "The consumer module cannot be mapped to exactly one statically discovered Gradle build."
                    : "The consumer module belongs to multiple discovered Gradle build models; project-path resolution is ambiguous.",
                consumer.Path));
            return Incomplete(diagnostics);
        }

        var owningBuild = owningBuilds[0].GradleModel!;
        if (!owningBuild.IsComplete)
        {
            complete = false;
            diagnostics.Add(CreateDiagnostic(
                "AndroidProjectDependencyBuildModelIncomplete",
                "The owning Gradle settings model is incomplete. Project dependency paths can be mapped only where an exact project declaration is present.",
                owningBuild.SettingsFile));
        }

        var androidProduction = new List<string>();
        var androidTest = new List<string>();
        var jvmProduction = new List<string>();
        var jvmTest = new List<string>();
        var seenAndroidProduction = new HashSet<string>(PathComparer);
        var seenAndroidTest = new HashSet<string>(PathComparer);
        var seenJvmProduction = new HashSet<string>(PathComparer);
        var seenJvmTest = new HashSet<string>(PathComparer);
        var orderedDependencies = new List<ResolvedProjectDependency>();

        foreach (var dependency in selection.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.ProjectPath))
                continue;

            var projectReferences = owningBuild.Projects
                .Where(project => project.ProjectPath.Equals(
                    NormalizeProjectPath(dependency.ProjectPath),
                    StringComparison.Ordinal))
                .ToArray();

            if (projectReferences.Length != 1)
            {
                complete = false;
                diagnostics.Add(CreateDiagnostic(
                    projectReferences.Length == 0
                        ? "AndroidProjectDependencyTargetNotDeclared"
                        : "AndroidProjectDependencyTargetAmbiguous",
                    projectReferences.Length == 0
                        ? $"Project dependency '{dependency.ProjectPath}' is not declared in the consumer's owning Gradle settings model."
                        : $"Project dependency '{dependency.ProjectPath}' resolves to multiple project declarations.",
                    consumer.Path));
                continue;
            }

            var targetPath = Path.GetFullPath(projectReferences[0].ProjectDirectory);
            var targetModules = modules
                .Where(module => PathsEqual(module.Path, targetPath))
                .ToArray();

            if (targetModules.Length != 1)
            {
                complete = false;
                diagnostics.Add(CreateDiagnostic(
                    targetModules.Length == 0
                        ? "AndroidProjectDependencyModuleNotDiscovered"
                        : "AndroidProjectDependencyModuleAmbiguous",
                    targetModules.Length == 0
                        ? $"Gradle project '{dependency.ProjectPath}' has no uniquely discovered project module."
                        : $"Gradle project '{dependency.ProjectPath}' maps to multiple discovered project modules.",
                    targetPath));
                continue;
            }

            var target = targetModules[0];
            if (target.BuildSystem != Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Gradle
                || target.GradleBuildModel is null)
            {
                complete = false;
                diagnostics.Add(CreateDiagnostic(
                    "AndroidProjectDependencyTargetBuildModelUnavailable",
                    $"The target module for '{dependency.ProjectPath}' has no complete Gradle build model; its compiler-output kind cannot be established.",
                    target.Path));
                continue;
            }

            if (!target.GradleBuildModel.IsComplete)
            {
                complete = false;
                diagnostics.Add(CreateDiagnostic(
                    "AndroidProjectDependencyTargetBuildModelIncomplete",
                    $"The Gradle build model for project '{dependency.ProjectPath}' is incomplete; compiler-output compatibility cannot be fully established.",
                    target.Path));
            }

            if (target.GradleBuildModel.Dependencies.Any(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.ProjectPath)))
            {
                complete = false;
                diagnostics.Add(CreateDiagnostic(
                    "AndroidProjectDependencyTransitiveOutputsUnresolved",
                    $"Project '{dependency.ProjectPath}' declares additional project dependencies. Their variant-compatible outputs are not inferred transitively in this resolution pass.",
                    target.Path));
            }

            var isTestDependency = IsTestConfiguration(dependency.Configuration);
            if (target.GradleBuildModel.Android is { IsComplete: false })
            {
                complete = false;
                diagnostics.Add(CreateDiagnostic(
                    "AndroidProjectDependencyTargetVariantModelIncomplete",
                    $"The Android variant model for project '{dependency.ProjectPath}' is incomplete.",
                    target.Path));
            }

            if (target.GradleBuildModel.Android is not null)
            {
                var matchingVariants = target.GradleBuildModel.Android.Variants
                    .Where(variant => variant.Name.Equals(selection.VariantName, StringComparison.Ordinal))
                    .ToArray();
                if (matchingVariants.Length != 1)
                {
                    complete = false;
                    diagnostics.Add(CreateDiagnostic(
                        matchingVariants.Length == 0
                            ? "AndroidProjectDependencyVariantIncompatible"
                            : "AndroidProjectDependencyVariantAmbiguous",
                        matchingVariants.Length == 0
                            ? $"Android project '{dependency.ProjectPath}' does not declare the exact consumer variant '{selection.VariantName}'. No producer variant is guessed."
                            : $"Android project '{dependency.ProjectPath}' declares an ambiguous '{selection.VariantName}' variant.",
                        target.Path));
                    continue;
                }

                if (AddPath(
                    target.Path,
                    isTestDependency ? androidTest : androidProduction,
                    isTestDependency ? seenAndroidTest : seenAndroidProduction))
                {
                    orderedDependencies.Add(new ResolvedProjectDependency(
                        target.Path,
                        selection.VariantName,
                        isTestDependency,
                        isTestDependency && (target.IsTestProject || target.IsTestSupport)));
                }
            }
            else
            {
                if (AddPath(
                    target.Path,
                    isTestDependency ? jvmTest : jvmProduction,
                    isTestDependency ? seenJvmTest : seenJvmProduction))
                {
                    orderedDependencies.Add(new ResolvedProjectDependency(
                        target.Path,
                        null,
                        isTestDependency,
                        isTestDependency && (target.IsTestProject || target.IsTestSupport)));
                }
            }
        }

        return new Result(
            androidProduction,
            androidTest,
            jvmProduction,
            jvmTest,
            orderedDependencies,
            diagnostics,
            complete && diagnostics.All(diagnostic => !diagnostic.AffectsCoverage));
    }

    /// <summary>
    /// Assembles host-validated compiler outputs in the original selected
    /// dependency declaration order. Android outputs require exact variant
    /// identity; ordinary JVM outputs remain unqualified.
    /// </summary>
    public ClasspathResult AssembleCompilationOutputs(
        ProjectModuleContext consumer,
        Result resolution,
        SemanticAnalysisExecutionContext executionContext,
        bool includeTestCode,
        string? variantName)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(executionContext);

        var diagnostics = resolution.Diagnostics.ToList();
        var entries = new List<string>();
        var seen = new HashSet<string>(PathComparer);

        AddEntries(executionContext.GetCompilationOutputs(
            consumer.Path,
            includeTestCode,
            variantName));

        foreach (var dependency in resolution.OrderedDependencies)
        {
            var productionEntries = executionContext.GetCompilationOutputs(
                dependency.ModulePath,
                false,
                dependency.VariantName);
            if (productionEntries.Count == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    "AndroidProjectDependencyCompilationOutputUnavailable",
                    $"No host-validated compiler output is registered for project dependency '{dependency.ModulePath}' in the required variant and scope.",
                    dependency.ModulePath));
                continue;
            }

            AddEntries(productionEntries);
            if (includeTestCode && dependency.IsTestDependency && dependency.IncludeTestOutput)
            {
                var testEntries = executionContext.GetCompilationOutputs(
                    dependency.ModulePath,
                    true,
                    dependency.VariantName);
                AddEntries(testEntries);
            }
        }

        return new ClasspathResult(
            entries,
            diagnostics,
            resolution.IsComplete && diagnostics.All(diagnostic => !diagnostic.AffectsCoverage));

        void AddEntries(IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                if (seen.Add(value))
                    entries.Add(value);
            }
        }
    }

    private static bool AddPath(
        string path,
        ICollection<string> paths,
        ISet<string> seen)
    {
        var normalized = Path.GetFullPath(path);
        if (!seen.Add(normalized))
            return false;

        paths.Add(normalized);
        return true;
    }

    private static bool IsTestConfiguration(string configuration)
    {
        var normalized = configuration
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized.StartsWith("test", StringComparison.Ordinal)
            || normalized.StartsWith("androidtest", StringComparison.Ordinal)
            || normalized.Contains("unittest", StringComparison.Ordinal)
            || normalized.Contains("androidtest", StringComparison.Ordinal)
            || normalized.Contains("testfixtures", StringComparison.Ordinal);
    }

    private static string NormalizeProjectPath(string projectPath)
    {
        var segments = projectPath.Split(
            ':',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? ":" : $":{string.Join(':', segments)}";
    }

    private static bool PathsEqual(string left, string right) =>
        PathComparer.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static AnalysisDiagnostic CreateDiagnostic(string code, string message, string? target) =>
        new()
        {
            Code = code,
            Component = nameof(AndroidVariantProjectDependencyResolver),
            Message = message,
            Target = target,
            AffectsCoverage = true
        };

    private static Result Incomplete(IReadOnlyList<AnalysisDiagnostic> diagnostics) =>
        new([], [], [], [], [], diagnostics, false);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public sealed record ResolvedProjectDependency(
        string ModulePath,
        string? VariantName,
        bool IsTestDependency,
        bool IncludeTestOutput);

    public sealed record Result(
        IReadOnlyList<string> AndroidProductionDependencies,
        IReadOnlyList<string> AndroidTestDependencies,
        IReadOnlyList<string> JvmProductionDependencies,
        IReadOnlyList<string> JvmTestDependencies,
        IReadOnlyList<ResolvedProjectDependency> OrderedDependencies,
        IReadOnlyList<AnalysisDiagnostic> Diagnostics,
        bool IsComplete);

    public sealed record ClasspathResult(
        IReadOnlyList<string> Entries,
        IReadOnlyList<AnalysisDiagnostic> Diagnostics,
        bool IsComplete);
}
