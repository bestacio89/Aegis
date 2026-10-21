namespace Aegis.Architecture.Analysis.Detectors;

internal static class FileExclusions
{
    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".idea", ".vscode",
            "bin", "obj", "node_modules", "dist", "build",
            "__pycache__", ".venv", "venv", "coverage", "TestResults",
            "Temp", "Logs", "PackageCache", "Generated",
            "TextMesh Pro", "TextMeshPro", "Examples & Extras",
            "ThirdParty", "Third-Party", "vendor"
        };

    public static bool IsExcluded(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        var normalized = path.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);

        var segments = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(ExcludedDirectories.Contains))
            return true;

        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Unity's Library directory is generated cache, but a directory named
        // Library in an ordinary .NET application may contain first-party code.
        // Exclude it only when its parent is demonstrably a Unity project root.
        var directory = Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path));

        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (Path.GetFileName(directory).Equals(
                    "Library",
                    StringComparison.OrdinalIgnoreCase))
            {
                var unityRoot = Directory.GetParent(directory)?.FullName;
                if (unityRoot is not null
                    && Directory.Exists(Path.Combine(unityRoot, "Assets"))
                    && (Directory.Exists(Path.Combine(unityRoot, "ProjectSettings"))
                        || Directory.Exists(Path.Combine(unityRoot, "Packages"))))
                {
                    return true;
                }
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return false;
    }
}
