using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Enums;

namespace Aegis.Core.Analysis.Detectors;

internal static class ConfidenceEvaluator
{
    public static void Evaluate(ProjectArchitectureContext ctx)
    {
        ctx.ConfidenceMap["Language"] = ctx.Language != "Unknown" ? 1.0 : 0.4;
        ctx.ConfidenceMap["Framework"] = !string.IsNullOrEmpty(ctx.Framework) ? 0.9 : 0.5;
        ctx.ConfidenceMap["BuildSystem"] = ctx.BuildSystem != BuildSystem.Unknown ? 0.9 : 0.5;
        ctx.ConfidenceMap["DomainType"] = ctx.DomainType != "Unknown" ? 0.85 : 0.5;
        ctx.ConfidenceMap["Layer"] = ctx.Layer != "Unknown" ? 0.8 : 0.4;
        ctx.ConfidenceMap["ArchitectureStyle"] = ctx.ArchitectureStyle != "Unknown" ? 0.75 : 0.5;
        ctx.ConfidenceMap["Deployability"] = ctx.IsDeployable ? 0.9 : 0.5;

        ctx.Confidence = Math.Clamp(ctx.ConfidenceMap.Values.DefaultIfEmpty(0.6).Average(), 0, 1);
    }
}
