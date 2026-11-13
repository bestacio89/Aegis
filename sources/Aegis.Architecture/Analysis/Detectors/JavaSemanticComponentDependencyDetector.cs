using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Compatibility entry point for the Java semantic-analysis pipeline.
/// Component projection is language-neutral and implemented by
/// <see cref="SemanticArchitectureComponentDetector"/>.
/// </summary>
internal static class JavaSemanticComponentDependencyDetector
{
    public static void Analyze(ProjectArchitectureContext context) =>
        SemanticArchitectureComponentDetector.Analyze(context);
}
