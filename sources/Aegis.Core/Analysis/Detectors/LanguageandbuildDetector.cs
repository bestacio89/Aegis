using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Enums;

namespace Aegis.Core.Analysis.Detectors;

internal static class LanguageAndBuildDetector
{
    public static void Analyze(ProjectArchitectureContext ctx, List<string> files)
    {
        if (files.Any(f => f.EndsWith(".csproj") || f.EndsWith(".sln")))
        {
            ctx.Language = "C#";
            ctx.BuildSystem = BuildSystem.DotNet;
        }
        else if (files.Any(f => f.EndsWith(".java") || f.Contains("pom.xml")))
        {
            ctx.Language = "Java";
            ctx.BuildSystem = BuildSystem.Maven;
        }
        else if (files.Any(f => f.EndsWith(".py") || f.Contains("setup.py") || f.Contains("pyproject.toml")))
        {
            ctx.Language = "Python";
            ctx.BuildSystem = BuildSystem.Pip;
        }
        else if (files.Any(f => f.EndsWith(".ts") || f.EndsWith(".tsx") || f.EndsWith(".js")))
        {
            ctx.Language = "TypeScript/JavaScript";
            if (files.Any(f => f.EndsWith("package.json"))) ctx.BuildSystem = BuildSystem.Npm;
            else ctx.BuildSystem = BuildSystem.Unknown;
        }
        else if (files.Any(f => f.EndsWith(".go")))
        {
            ctx.Language = "Go";
            ctx.BuildSystem = BuildSystem.GoMod;
        }
        else if (files.Any(f => f.EndsWith(".rs") || f.Contains("Cargo.toml")))
        {
            ctx.Language = "Rust";
            ctx.BuildSystem = BuildSystem.Cargo;
        }
        else
        {
            ctx.Language = "Unknown";
            ctx.BuildSystem = BuildSystem.Unknown;
        }

        ctx.InferenceHistory.Add(new ArchitectureInferenceTrace
        {
            Key = "Language",
            Value = ctx.Language,
            Source = InferenceSource.FileSystem,
            Confidence = ctx.Language == "Unknown" ? 0.4 : 0.95
        });
    }
}
