using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class DomainDetector
{
    public static void Analyze(ProjectArchitectureContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var projectKinds = ctx.Modules
            .Select(module => module.ProjectKind)
            .Where(kind => !string.IsNullOrWhiteSpace(kind) && kind != "Unknown")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
                "Test" => "Test",
                "Executable" => "Application",
                _ => ctx.DomainType ?? "Unknown"
            };

            if (ctx.DomainType != "Unknown")
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
