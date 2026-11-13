using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

public interface IJavaClasspathResolver
{
    JavaClasspathResolution Resolve(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectModuleContext> projectModules,
        bool includeTestCode);
}
