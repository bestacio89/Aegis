using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Core.Architecture.Analysis.Detectors;

internal static class LanguageAndBuildDetector
{
    public static void Analyze(ProjectArchitectureContext ctx, List<string> files)
    {
        if (files.Any(f => f.EndsWith(".csproj") || f.EndsWith(".sln")))
        {
            ctx.Language = "C#";
            ctx.BuildSystem = ArchitectureBuildSystem.DotNet;
        }
        else if (files.Any(f => f.EndsWith(".java") || f.Contains("pom.xml")))
        {
            ctx.Language = "Java";
            ctx.BuildSystem = ArchitectureBuildSystem.Maven;
        }
        else if (files.Any(f => f.EndsWith(".py") || f.Contains("setup.py") || f.Contains("pyproject.toml")))
        {
            ctx.Language = "Python";
            ctx.BuildSystem = ArchitectureBuildSystem.Pip;
        }
        else if (files.Any(f => f.EndsWith(".ts") || f.EndsWith(".tsx") || f.EndsWith(".js")))
        {
            ctx.Language = "TypeScript/JavaScript";
            if (files.Any(f => f.EndsWith("package.json"))) ctx.BuildSystem = ArchitectureBuildSystem.Npm;
            else ctx.BuildSystem = ArchitectureBuildSystem.Unknown;
        }
        else if (files.Any(f => f.EndsWith(".go")))
        {
            ctx.Language = "Go";
            ctx.BuildSystem = ArchitectureBuildSystem.GoMod;
        }
        else if (files.Any(f => f.EndsWith(".rs") || f.Contains("Cargo.toml")))
        {
            ctx.Language = "Rust";
            ctx.BuildSystem = ArchitectureBuildSystem.Cargo;
        }
        else
        {
            ctx.Language = "Unknown";
            ctx.BuildSystem = ArchitectureBuildSystem.Unknown;
        }

        ctx.InferenceHistory.Add(new ArchitectureInferenceTrace
        {
            Key = "Language",
            Value = ctx.Language,
            Source = ArchitectureInferenceSource.FileSystem,
            Confidence = ctx.Language == "Unknown" ? 0.4 : 0.95
        });
    }
}
