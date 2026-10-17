using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class ArchitectureConfidenceEvaluator
{
    public static void Evaluate(ProjectArchitectureContext ctx)
    {
        ctx.ConfidenceMap["Language"] = ctx.Language != "Unknown" ? 1.0 : 0.4;
        ctx.ConfidenceMap["Framework"] = !string.IsNullOrEmpty(ctx.Framework) ? 0.9 : 0.5;
        ctx.ConfidenceMap["BuildSystem"] = ctx.BuildSystem != ArchitectureBuildSystem.Unknown ? 0.9 : 0.5;
        ctx.ConfidenceMap["DomainType"] = ctx.DomainType != "Unknown" ? 0.85 : 0.5;
        ctx.ConfidenceMap["Layers"] = ctx.Modules.Any(m => m.Layers.Any()) ? 0.85 : 0.4;

        ctx.ConfidenceMap["ArchitectureStyle"] =
     !string.IsNullOrWhiteSpace(ctx.ArchitectureStyle)
         ? 0.75
         : 0.5;  
        ctx.ConfidenceMap["Deployability"] = ctx.IsDeployable ? 0.9 : 0.5;
        ctx.ConfidenceMap["Modules"] =
        ctx.Modules.Any()
        ? 0.9
        : 0.4;
        ctx.Confidence = Math.Clamp(ctx.ConfidenceMap.Values.DefaultIfEmpty(0.6).Average(), 0, 1);
    }
}
