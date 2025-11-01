using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class DomainAndLayerDetector
{
    public static void Analyze(ProjectArchitectureContext ctx, List<string> files)
    {
        if (ctx.Language.Contains("TypeScript") || ctx.Language == "JavaScript")
        {
            ctx.DomainType = "Frontend";
            ctx.Layer = "UI";
        }
        else if (ctx.Language is "C#" or "Java" or "Python")
        {
            ctx.DomainType = "Backend";
            InferBackendLayer(ctx, files);
        }
        else if (files.Any(IsIaCFile))
        {
            ctx.DomainType = "Infrastructure";
            ctx.Layer = "Deployment";
        }
        else ctx.DomainType = "Unknown";
    }

    private static void InferBackendLayer(ProjectArchitectureContext ctx, List<string> files)
    {
        var dirs = files
            .Select(f => Path.GetDirectoryName(f))
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => Path.GetFileName(d!) ?? string.Empty)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name.ToLowerInvariant())
            .ToList();

        if (dirs.Any(d => d.Contains("api") || d.Contains("controller")))
            ctx.Layer = "Api";
        else if (dirs.Any(d => d.Contains("application") || d.Contains("service")))
            ctx.Layer = "Application";
        else if (dirs.Any(d => d.Contains("domain") || d.Contains("core")))
            ctx.Layer = "Domain";
        else if (dirs.Any(d => d.Contains("infra") || d.Contains("repository")))
            ctx.Layer = "Infrastructure";
        else
            ctx.Layer = "Unknown";
    }

    private static bool IsIaCFile(string f)
    {
        var name = Path.GetFileName(f).ToLowerInvariant();
        return name is "dockerfile" or "docker-compose.yml" or "docker-compose.yaml"
            || name.EndsWith(".bicep") || name.EndsWith(".tf")
            || name.EndsWith(".yaml") && (name.Contains("deployment") || name.Contains("service"));
    }
}
