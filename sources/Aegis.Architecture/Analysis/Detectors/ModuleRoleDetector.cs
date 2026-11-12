using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Resolves module responsibilities from explicit project metadata and detected
/// technologies. Names are used only as supporting evidence for shared libraries.
/// </summary>
internal static class ModuleRoleDetector
{
    private static readonly HashSet<string> SharedNameTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "shared",
        "common",
        "commons",
        "abstractions",
        "contracts"
    };

    private static readonly HashSet<string> FrontendFrameworks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Angular",
        "React",
        "Vue",
        "Next.js",
        "Nuxt",
        "Blazor"
    };

    private static readonly HashSet<string> BackendFrameworks = new(StringComparer.OrdinalIgnoreCase)
    {
        "ASP.NET Core",
        "Express",
        "NestJS",
        "Fastify",
        "Koa",
        "Hapi",
        "Hono",
        "Spring Boot",
        "Quarkus",
        "Micronaut",
        "Ktor"
    };

    public static void Analyze(IReadOnlyCollection<ProjectModuleContext> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var module in modules)
        {
            module.Roles.Clear();

            if (module.IsTestProject
                || module.ProjectKind.Equals("Test", StringComparison.OrdinalIgnoreCase))
            {
                AddRole(module, ProjectModuleRole.Test);
                continue;
            }

            if (module.IsTestSupport
                || module.ProjectKind.Equals("TestSupport", StringComparison.OrdinalIgnoreCase))
            {
                AddRole(module, ProjectModuleRole.TestSupport);
                AddRole(module, ProjectModuleRole.Shared);
                continue;
            }

            var isFrontend = module.ProjectKind.Equals("FrontendApplication", StringComparison.OrdinalIgnoreCase)
                || module.Frameworks.Any(FrontendFrameworks.Contains);
            var isBackend = module.ProjectKind.Equals("BackendApplication", StringComparison.OrdinalIgnoreCase)
                || module.Frameworks.Any(BackendFrameworks.Contains)
                || (module.IsWebProject && !isFrontend);

            if (module.ProjectKind.Equals("FullStackApplication", StringComparison.OrdinalIgnoreCase))
            {
                isFrontend = true;
                isBackend = true;
            }

            if (isBackend)
                AddRole(module, ProjectModuleRole.Backend);
            if (isFrontend)
                AddRole(module, ProjectModuleRole.Frontend);

            if (module.ProjectKind.Equals("DesktopApplication", StringComparison.OrdinalIgnoreCase))
                AddRole(module, ProjectModuleRole.Desktop);

            if (module.IsLibrary)
            {
                AddRole(module, ProjectModuleRole.Library);

                if (HasSharedNameEvidence(module.Name)
                    || module.ProjectKind.Contains("Shared", StringComparison.OrdinalIgnoreCase))
                {
                    AddRole(module, ProjectModuleRole.Shared);
                }
            }

            if (module.IsExecutable)
                AddRole(module, ProjectModuleRole.Executable);

            if (module.Roles.Count == 0)
                AddRole(module, ProjectModuleRole.Unknown);
        }
    }

    private static bool HasSharedNameEvidence(string name) =>
        name.Split(['.', '-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(SharedNameTokens.Contains);

    private static void AddRole(ProjectModuleContext module, ProjectModuleRole role)
    {
        if (!module.Roles.Contains(role))
            module.Roles.Add(role);
    }
}
