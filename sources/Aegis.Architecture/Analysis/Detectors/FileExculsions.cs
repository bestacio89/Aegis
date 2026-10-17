namespace Aegis.Architecture.Analysis.Detectors;

internal static class FileExclusions
{
    private static readonly string[] ExcludedDirs =
        [".git", ".vs", ".idea", "bin", "obj", "node_modules", "dist", "build", "__pycache__"];

    public static bool IsExcluded(string path)
    {
        var lower = path.ToLowerInvariant();
        return ExcludedDirs.Any(ex => lower.Contains($"{Path.DirectorySeparatorChar}{ex}{Path.DirectorySeparatorChar}"));
    }
}
