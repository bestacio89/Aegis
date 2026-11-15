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
        IReadOnlyCollection<GradleBuildModel.Dependency> selectedGradleDependencies) =>
        Resolve(module, projectModules, includeTestCode: false);
}
