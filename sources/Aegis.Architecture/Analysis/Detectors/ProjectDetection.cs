namespace Aegis.Architecture.Analysis.Detectors;

internal static class ProjectDiscovery
{
    private static readonly string[] ManifestFiles =
        ["*.csproj", "pom.xml", "build.gradle", "build.gradle.kts",
         "package.json", "setup.py", "pyproject.toml", "requirements.txt",
         "go.mod", "Cargo.toml"];

    public static List<string> FindProjectRoots(List<string> allFiles)
    {
        var roots = allFiles
            .Where(f => ManifestFiles.Any(m => f.EndsWith(m, StringComparison.OrdinalIgnoreCase)))
            .Select(f => Path.GetDirectoryName(f)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return roots;
    }
}
