using Aegis.Shared.Architecture.Models;

namespace Aegis.Core.Analysis.Detectors
{
    internal static class LayerDetector
    {
        public static void Analyze(ProjectArchitectureContext ctx)
        {
            foreach (var module in ctx.Modules)
            {
                // The layer IS the real project/module (e.g. "Aegis.Core", "Aegis.Shared",
                // "Aegis.App.WPF") — not a guessed semantic bucket like "Api"/"Domain"/
                // "Infrastructure" derived from keyword matching on the folder name.
                //
                // Keyword matching meant any project whose name didn't happen to contain
                // one of a handful of English substrings collapsed into a single "Unknown"
                // bucket together with every other unmatched project — exactly the
                // "combined stuff that doesn't work" problem. Using the real project name
                // means every distinct project stays its own distinct, honestly-labeled
                // layer, with no guessing and no shared catch-all bucket.
                module.Layers.Add(
                    new ProjectLayerContext
                    {
                        Name = module.Name,
                        Path = module.Path,
                        Confidence = 1.0
                    });
            }
        }
    }
}