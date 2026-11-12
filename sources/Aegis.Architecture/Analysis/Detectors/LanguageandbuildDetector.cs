using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class LanguageAndBuildDetector
{
    public static void Analyze(ProjectArchitectureContext context, List<string> files)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(files);

        if (files.Any(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            context.Language = "C#";
            context.BuildSystem = ArchitectureBuildSystem.DotNet;
        }
        else if (files.Any(file => Path.GetExtension(file).Equals(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            context.Language = "C#";
            context.BuildSystem = ArchitectureBuildSystem.Unknown;
        }
        else if (HasJavaEvidence(files))
        {
            context.Language = "Java";
            context.BuildSystem = ResolveJavaBuildSystem(files);
        }
        else if (files.Any(file =>
                     Path.GetExtension(file).Equals(".py", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(file).Equals("setup.py", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(file).Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)))
        {
            context.Language = "Python";
            context.BuildSystem = ResolvePythonBuildSystem(files);
        }
        else if (files.Any(file =>
                     Path.GetExtension(file).Equals(".ts", StringComparison.OrdinalIgnoreCase)
                     || Path.GetExtension(file).Equals(".tsx", StringComparison.OrdinalIgnoreCase)
                     || Path.GetExtension(file).Equals(".js", StringComparison.OrdinalIgnoreCase)))
        {
            context.Language = "TypeScript/JavaScript";
            context.BuildSystem = ResolveNodeBuildSystem(files);
        }
        else if (files.Any(file => Path.GetExtension(file).Equals(".go", StringComparison.OrdinalIgnoreCase)))
        {
            context.Language = "Go";
            context.BuildSystem = ArchitectureBuildSystem.GoMod;
        }
        else if (files.Any(file =>
                     Path.GetExtension(file).Equals(".rs", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(file).Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)))
        {
            context.Language = "Rust";
            context.BuildSystem = ArchitectureBuildSystem.Cargo;
        }
        else
        {
            context.Language = "Unknown";
            context.BuildSystem = ArchitectureBuildSystem.Unknown;
        }

        context.InferenceHistory.Add(new ArchitectureInferenceTrace
        {
            Key = "Language",
            Value = context.Language,
            Source = ArchitectureInferenceSource.FileSystem,
            Confidence = context.Language == "Unknown" ? 0.4 : 0.95
        });

        context.InferenceHistory.Add(new ArchitectureInferenceTrace
        {
            Key = "BuildSystem",
            Value = context.BuildSystem.ToString(),
            Source = ArchitectureInferenceSource.FileSystem,
            Confidence = context.BuildSystem == ArchitectureBuildSystem.Unknown ? 0.4 : 0.95
        });
    }

    private static bool HasJavaEvidence(IReadOnlyCollection<string> files) =>
        files.Any(file =>
            Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase)
            || IsMavenManifest(file)
            || IsGradleManifest(file));

    private static ArchitectureBuildSystem ResolveJavaBuildSystem(
        IReadOnlyCollection<string> files)
    {
        var hasMaven = files.Any(IsMavenManifest);
        var hasGradle = files.Any(IsGradleManifest);

        return (hasMaven, hasGradle) switch
        {
            (true, false) => ArchitectureBuildSystem.Maven,
            (false, true) => ArchitectureBuildSystem.Gradle,
            _ => ArchitectureBuildSystem.Unknown
        };
    }

    internal static bool IsMavenManifest(string file) =>
        Path.GetFileName(file).Equals("pom.xml", StringComparison.OrdinalIgnoreCase);

    internal static bool IsGradleManifest(string file)
    {
        var name = Path.GetFileName(file);
        return name.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase)
            || name.Equals("settings.gradle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("settings.gradle.kts", StringComparison.OrdinalIgnoreCase);
    }
}
