using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Plans compiler-owned source scopes for Kotlin Multiplatform targets.
/// Only target backends with validated semantic execution are marked supported.
/// </summary>
public sealed class KotlinMultiplatformSemanticAnalysisPlanner
{
    public Plan Resolve(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectSourceRoot> sourceRoots)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(sourceRoots);

        var model = module.GradleBuildModel?.KotlinMultiplatform;
        if (model is null)
        {
            var diagnostic = CreateDiagnostic(
                "KotlinMultiplatformBuildModelUnavailable",
                "The Kotlin Multiplatform build model is unavailable; no target compilation scope can be selected.",
                module.Path);
            return new Plan([], [diagnostic]);
        }

        var diagnostics = model.Diagnostics.ToList();
        var scopes = new List<TargetScope>();
        if (model.Targets.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                "KotlinMultiplatformTargetsNotResolved",
                "No Kotlin Multiplatform targets were resolved from the build model.",
                module.Path));
            return new Plan(scopes, diagnostics);
        }

        foreach (var target in model.Targets)
        {
            if (!model.IsComplete)
            {
                scopes.Add(CreateUnsupportedScope(
                    target,
                    "KotlinMultiplatformSourceSetGraphIncomplete",
                    "The source-set graph is incomplete or contains unsupported Gradle configuration. Target compilation was withheld rather than using an inferred source scope."));
                continue;
            }

            if (target.Platform != "JVM")
            {
                scopes.Add(CreateUnsupportedScope(
                    target,
                    "KotlinMultiplatformTargetBackendNotSupported",
                    $"No validated semantic execution backend is registered for the Kotlin Multiplatform target '{target.Name}' ({target.Platform})."));
                continue;
            }

            if (!TryResolveSourceSetClosure(model, $"{target.Name}Main", out var productionSets)
                || !TryResolveSourceSetClosure(model, $"{target.Name}Test", out var testSets))
            {
                scopes.Add(CreateUnsupportedScope(
                    target,
                    "KotlinMultiplatformTargetSourceSetsUnavailable",
                    $"The source-set closure for target '{target.Name}' could not be resolved completely."));
                continue;
            }

            if (!TryResolveRoots(
                    module.Path, productionSets, "Production", sourceRoots,
                    out var productionRoots, out var productionRootDiagnostic))
            {
                scopes.Add(new TargetScope(
                    target.Name, target.Platform, false,
                    [], [], [], [], productionSets.Select(sourceSet => sourceSet.Name).ToArray(),
                    testSets.Select(sourceSet => sourceSet.Name).ToArray(),
                    productionRootDiagnostic));
                continue;
            }

            if (!TryResolveRoots(
                    module.Path, testSets, "Test", sourceRoots,
                    out var testRoots, out var testRootDiagnostic))
            {
                scopes.Add(new TargetScope(
                    target.Name, target.Platform, false,
                    productionRoots, [], [], [], productionSets.Select(sourceSet => sourceSet.Name).ToArray(),
                    testSets.Select(sourceSet => sourceSet.Name).ToArray(),
                    testRootDiagnostic));
                continue;
            }

            var productionSharedSets = productionSets
                .Where(sourceSet => sourceSet.Kind is "Common" or "Intermediate")
                .ToArray();
            var testSharedSets = testSets
                .Where(sourceSet => sourceSet.Kind is "Common" or "Intermediate")
                .ToArray();

            var productionSharedResolved = TryResolveRoots(
                module.Path, productionSharedSets, "Production", sourceRoots,
                out var productionSharedRoots, out var productionSharedRootDiagnostic);
            var testSharedResolved = TryResolveRoots(
                module.Path, testSharedSets, "Test", sourceRoots,
                out var testSharedRoots, out var testSharedRootDiagnostic);
            if (!productionSharedResolved || !testSharedResolved)
            {
                scopes.Add(CreateUnsupportedScope(
                    target,
                    "KotlinMultiplatformSharedSourceRootsUnresolved",
                    productionSharedRootDiagnostic?.Message
                        ?? testSharedRootDiagnostic?.Message
                        ?? "Shared source roots could not be resolved for the target."));
                continue;
            }

            scopes.Add(new TargetScope(
                target.Name,
                target.Platform,
                true,
                productionRoots,
                testRoots,
                productionSharedRoots,
                testSharedRoots,
                productionSets.Select(sourceSet => sourceSet.Name).ToArray(),
                testSets.Select(sourceSet => sourceSet.Name).ToArray(),
                null));
        }

        diagnostics.AddRange(scopes
            .Where(scope => !scope.IsSupported && scope.Diagnostic is not null)
            .Select(scope => scope.Diagnostic!));
        return new Plan(scopes, diagnostics);
    }

    private static bool TryResolveSourceSetClosure(
        KotlinMultiplatformBuildModel model,
        string rootName,
        out IReadOnlyList<KotlinMultiplatformBuildModel.SourceSet> closure)
    {
        var byName = model.SourceSets
            .GroupBy(sourceSet => sourceSet.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var valid = true;

        bool Visit(string name)
        {
            if (!byName.TryGetValue(name, out var sourceSet))
            {
                valid = false;
                return false;
            }

            if (!visited.Add(name))
                return true;

            foreach (var dependency in sourceSet.DependsOn)
                Visit(dependency);

            return valid;
        }

        if (!Visit(rootName) || !valid)
        {
            closure = Array.Empty<KotlinMultiplatformBuildModel.SourceSet>();
            return false;
        }

        closure = model.SourceSets.Where(sourceSet => visited.Contains(sourceSet.Name)).ToArray();
        return true;
    }

    private static bool TryResolveRoots(
        string modulePath,
        IReadOnlyCollection<KotlinMultiplatformBuildModel.SourceSet> sourceSets,
        string ownership,
        IReadOnlyCollection<ProjectSourceRoot> sourceRoots,
        out IReadOnlyList<ProjectSourceRoot> roots,
        out AnalysisDiagnostic? diagnostic)
    {
        var selected = new List<ProjectSourceRoot>();
        foreach (var sourceSet in sourceSets.Where(sourceSet => sourceSet.Ownership == ownership))
        {
            var conventionalDirectory = Path.GetFullPath(Path.Combine(
                modulePath, "src", sourceSet.Name, "kotlin"));
            IReadOnlyList<string> expectedDirectories = sourceSet.KotlinSourceDirectories is { Count: > 0 } explicitDirectories
                ? explicitDirectories
                : [conventionalDirectory];

            foreach (var directory in expectedDirectories)
            {
                var expectedDirectory = Path.GetFullPath(directory);
                var root = sourceRoots.FirstOrDefault(candidate =>
                    PathComparer.Equals(Path.GetFullPath(candidate.Directory), expectedDirectory));
                if (root is null)
                    continue;

                var expectedOwnership = ownership == "Production"
                    ? ProjectSourceRootOwnership.Production
                    : ProjectSourceRootOwnership.Test;
                if (root.Ownership != expectedOwnership || !root.ContainsLanguage("Kotlin"))
                {
                    roots = Array.Empty<ProjectSourceRoot>();
                    diagnostic = CreateDiagnostic(
                        "KotlinMultiplatformSourceRootOwnershipUnresolved",
                        $"Source root '{expectedDirectory}' does not have authoritative {ownership.ToLowerInvariant()} Kotlin ownership.",
                        expectedDirectory);
                    return false;
                }

                selected.Add(root);
            }
        }

        roots = selected
            .DistinctBy(root => Path.GetFullPath(root.Directory), PathComparer)
            .ToArray();
        diagnostic = null;
        return true;
    }

    private static TargetScope CreateUnsupportedScope(
        KotlinMultiplatformBuildModel.Target target,
        string code,
        string message)
    {
        var diagnostic = CreateDiagnostic(code, message, target.Name);
        return new TargetScope(
            target.Name, target.Platform, false, [], [], [], [], [], [], diagnostic);
    }

    private static AnalysisDiagnostic CreateDiagnostic(string code, string message, string target) => new()
    {
        Code = code,
        Component = nameof(KotlinMultiplatformSemanticAnalysisPlanner),
        Message = message,
        Target = target,
        AffectsCoverage = true
    };

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public sealed record TargetScope(
        string TargetName,
        string Platform,
        bool IsSupported,
        IReadOnlyList<ProjectSourceRoot> ProductionSourceRoots,
        IReadOnlyList<ProjectSourceRoot> TestSourceRoots,
        IReadOnlyList<ProjectSourceRoot> ProductionSharedSourceRoots,
        IReadOnlyList<ProjectSourceRoot> TestSharedSourceRoots,
        IReadOnlyList<string> ProductionSourceSetNames,
        IReadOnlyList<string> TestSourceSetNames,
        AnalysisDiagnostic? Diagnostic);

    public sealed record Plan(
        IReadOnlyList<TargetScope> Targets,
        IReadOnlyList<AnalysisDiagnostic> Diagnostics);
}
