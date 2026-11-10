using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class DomainDetector
{
    public static void Analyze(ProjectArchitectureContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var applicationModules = ctx.Modules
            .Where(module => !module.IsTestProject
                && !string.Equals(module.ProjectKind, "Test", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var projectKinds = applicationModules
            .Select(module => module.ProjectKind)
            .Where(kind => !string.IsNullOrWhiteSpace(kind) && kind != "Unknown")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (projectKinds.Contains("WebApplication", StringComparer.OrdinalIgnoreCase)
            && projectKinds.Contains("FrontendApplication", StringComparer.OrdinalIgnoreCase))
        {
            ctx.DomainType = "FullStack";
            return;
        }

        // Supporting class libraries do not turn a web or frontend application
        // into a heterogeneous product. They are part of its project topology.
        if (projectKinds.Contains("WebApplication", StringComparer.OrdinalIgnoreCase)
            && projectKinds.All(kind => kind is "WebApplication" or "ClassLibrary"))
        {
            ctx.DomainType = applicationModules.Any(module =>
                module.Frameworks.Contains("Blazor", StringComparer.OrdinalIgnoreCase))
                    ? "FullStack"
                    : "Backend";
            return;
        }

        if (projectKinds.Contains("FrontendApplication", StringComparer.OrdinalIgnoreCase)
            && projectKinds.All(kind => kind is "FrontendApplication" or "ClassLibrary"))
        {
            ctx.DomainType = "Frontend";
            return;
        }

        if (projectKinds.Contains("DesktopApplication", StringComparer.OrdinalIgnoreCase)
            && projectKinds.All(kind => kind is "DesktopApplication" or "ClassLibrary"))
        {
            ctx.DomainType = "Desktop";
            return;
        }

        if (projectKinds.Length > 1)
        {
            ctx.DomainType = "Mixed";
            return;
        }

        if (projectKinds.Length == 1)
        {
            ctx.DomainType = projectKinds[0] switch
            {
                "ClassLibrary" => "Library",
                "WebApplication" => "Backend",
                "FrontendApplication" => "Frontend",
                "DesktopApplication" => "Desktop",
                "Executable" => "Application",
                _ => ctx.DomainType ?? "Unknown"
            };

            if (ctx.DomainType != "Unknown")
                return;
        }

        if (ctx.Modules.Count > 0
            && ctx.Modules.All(module => module.IsTestProject
                || string.Equals(module.ProjectKind, "Test", StringComparison.OrdinalIgnoreCase)))
        {
            ctx.DomainType = "Test";
            return;
        }

        var isBlazor = string.Equals(
                ctx.Framework,
                "Blazor",
                StringComparison.OrdinalIgnoreCase)
            || ctx.DetectedFrameworks.Any(framework =>
                string.Equals(
                    framework,
                    "Blazor",
                    StringComparison.OrdinalIgnoreCase));

        var isUnity = ctx.DetectedFrameworks.Any(framework =>
            framework.Equals("Unity", StringComparison.OrdinalIgnoreCase));

        var hasBackendFramework = ctx.DetectedFrameworks.Any(framework =>
            framework.Contains("ASP.NET Core", StringComparison.OrdinalIgnoreCase)
            || framework.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
            || framework.Contains(".NET (EF Core)", StringComparison.OrdinalIgnoreCase));

        // C# alone is not sufficient evidence for a backend classification:
        // Blazor is a UI framework. If both Blazor and server framework evidence
        // are present, preserve the mixed nature instead of hiding either side.
        if ((isBlazor || isUnity) && hasBackendFramework)
        {
            ctx.DomainType = "FullStack";
            return;
        }

        if (isUnity)
        {
            ctx.DomainType = "Game";
            return;
        }

        if (isBlazor)
        {
            ctx.DomainType = "Frontend";
            return;
        }

        ctx.DomainType =
            ctx.Language switch
            {
                "TypeScript" or "TypeScript/JavaScript" => "Frontend",

                "C#" or
                "Java" or
                "Python" => "Backend",

                _ => "Unknown"
            };
    }
}
