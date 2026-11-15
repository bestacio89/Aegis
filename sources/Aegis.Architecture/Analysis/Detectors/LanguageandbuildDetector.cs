using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class LanguageAndBuildDetector
{
    public static void Analyze(ProjectArchitectureContext context, List<string> files)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(files);

        var languages = DetectLanguages(files);
        context.Language = languages.Count switch
        {
            0 => "Unknown",
            1 => languages[0],
            _ => "Multi-Language"
        };

        var buildSystems = DetectBuildSystems(files);
        context.BuildSystem = buildSystems.Count == 1
            ? buildSystems[0]
            : ArchitectureBuildSystem.Unknown;

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

    private static List<string> DetectLanguages(IReadOnlyCollection<string> files)
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (files.Any(file =>
                file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(file).Equals(".cs", StringComparison.OrdinalIgnoreCase)))
            languages.Add("C#");

        if (files.Any(file =>
                Path.GetExtension(file).Equals(".kt", StringComparison.OrdinalIgnoreCase)
                || (Path.GetExtension(file).Equals(".kts", StringComparison.OrdinalIgnoreCase)
                    && !IsGradleManifest(file))))
            languages.Add("Kotlin");

        if (files.Any(file =>
                Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase)))
            languages.Add("Java");

        if (files.Any(file =>
                Path.GetExtension(file).Equals(".py", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("setup.py", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)))
            languages.Add("Python");

        if (files.Any(file =>
                Path.GetExtension(file).Equals(".ts", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(file).Equals(".tsx", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(file).Equals(".mts", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(file).Equals(".cts", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(file).Equals(".js", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(file).Equals(".jsx", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(file).Equals(".mjs", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(file).Equals(".cjs", StringComparison.OrdinalIgnoreCase)))
            languages.Add("TypeScript/JavaScript");

        if (files.Any(file => Path.GetExtension(file).Equals(".go", StringComparison.OrdinalIgnoreCase)))
            languages.Add("Go");

        if (files.Any(file =>
                Path.GetExtension(file).Equals(".rs", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)))
            languages.Add("Rust");

        return languages
            .OrderBy(language => language, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ArchitectureBuildSystem> DetectBuildSystems(
        IReadOnlyCollection<string> files)
    {
        var buildSystems = new HashSet<ArchitectureBuildSystem>();

        if (files.Any(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
            buildSystems.Add(ArchitectureBuildSystem.DotNet);
        if (files.Any(IsMavenManifest))
            buildSystems.Add(ArchitectureBuildSystem.Maven);
        if (files.Any(IsGradleManifest))
            buildSystems.Add(ArchitectureBuildSystem.Gradle);

        if (files.Any(file => Path.GetFileName(file).Equals("package.json", StringComparison.OrdinalIgnoreCase)))
        {
            if (files.Any(file => Path.GetFileName(file).Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase)))
                buildSystems.Add(ArchitectureBuildSystem.Pnpm);
            else if (files.Any(file => Path.GetFileName(file).Equals("yarn.lock", StringComparison.OrdinalIgnoreCase)))
                buildSystems.Add(ArchitectureBuildSystem.Yarn);
            else
                buildSystems.Add(ArchitectureBuildSystem.Npm);
        }

        if (files.Any(file =>
                Path.GetExtension(file).Equals(".py", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("setup.py", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)))
        {
            buildSystems.Add(files.Any(file => Path.GetFileName(file).Equals("poetry.lock", StringComparison.OrdinalIgnoreCase))
                ? ArchitectureBuildSystem.Poetry
                : ArchitectureBuildSystem.Pip);
        }

        if (files.Any(file =>
                Path.GetExtension(file).Equals(".rs", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)))
            buildSystems.Add(ArchitectureBuildSystem.Cargo);
        if (files.Any(file =>
                Path.GetExtension(file).Equals(".go", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("go.mod", StringComparison.OrdinalIgnoreCase)))
            buildSystems.Add(ArchitectureBuildSystem.GoMod);

        return buildSystems.OrderBy(value => value).ToList();
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
