using Aegis.Shared.Architecture.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.Core.Analysis.Detectors
{
    internal static class LayerDetector
    {
        public static void Analyze(ProjectArchitectureContext ctx)
        {
            foreach (var module in ctx.Modules)
            {
                var name = module.Name.ToLowerInvariant();


                var layer =
                    name.Contains("api")
                        ? "Api" :

                    name.Contains("application")
                        ? "Application" :

                    name.Contains("contract")
                        ? "Contracts" :

                    name.Contains("domain")
                        ? "Domain" :

                    name.Contains("infra")
                        ? "Infrastructure" :

                    "Unknown";


                module.Layers.Add(
                    new ProjectLayerContext
                    {
                        Name = layer,
                        Path = module.Path,
                        Confidence = 0.85
                    });
            }
        }
    }
}
