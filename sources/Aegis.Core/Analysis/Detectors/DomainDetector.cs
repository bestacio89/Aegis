using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class DomainDetector
{
    public static void Analyze(ProjectArchitectureContext ctx)
    {
        ctx.DomainType =
            ctx.Language switch
            {
                "TypeScript" => "Frontend",

                "C#" or
                "Java" or
                "Python" => "Backend",

                _ => "Unknown"
            };
    }
}