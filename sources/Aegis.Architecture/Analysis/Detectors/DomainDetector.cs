using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class DomainDetector
{
    public static void Analyze(ProjectArchitectureContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var isBlazor = string.Equals(
                ctx.Framework,
                "Blazor",
                StringComparison.OrdinalIgnoreCase)
            || ctx.DetectedFrameworks.Any(framework =>
                string.Equals(
                    framework,
                    "Blazor",
                    StringComparison.OrdinalIgnoreCase));

        // Blazor is a UI framework built on C#, so language-only classification
        // incorrectly labels every Blazor project as backend. Framework evidence
        // takes precedence for the project's primary domain.
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
