using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

public interface IJvmClasspathResolver
{
    JvmClasspathResolution Resolve(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectModuleContext> projectModules,
        bool includeTestCode);

    JvmClasspathResolution Resolve(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectModuleContext> projectModules,
        IReadOnlyCollection<GradleBuildModel.Dependency> selectedGradleDependencies)
    {
        ArgumentNullException.ThrowIfNull(selectedGradleDependencies);
        var unresolved = selectedGradleDependencies
            .Where(dependency => string.IsNullOrWhiteSpace(dependency.ProjectPath))
            .Select(dependency => (dependency.Group ?? "unknown") + ":"
                + (dependency.Artifact ?? "unknown") + ":"
                + (dependency.Version ?? "unspecified"))
            .ToArray();
        return new JvmClasspathResolution(
            [],
            unresolved,
            selectedGradleDependencies.Count > 0,
            unresolved.Length == 0);
    }
}
