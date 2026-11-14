using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

public interface IJvmClasspathResolver
{
    JvmClasspathResolution Resolve(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectModuleContext> projectModules,
        bool includeTestCode);
}
