namespace Aegis.Architecture.Analysis.Detectors;

internal static class FileExclusions
{
    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".idea", ".vscode",
            "bin", "obj", "node_modules", "dist", "build",
            "__pycache__", ".venv", "venv", "coverage", "TestResults",
            "Logs", "PackageCache", "Generated",
            "ThirdParty", "Third-Party", "vendor", ".nuget"
        };

    private static bool IsUnityTempPath(string path)
    {
        var current = Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path));

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Path.GetFileName(current).Equals(
                    "Temp",
                    StringComparison.OrdinalIgnoreCase))
            {
                var root = Directory.GetParent(current)?.FullName;
                return root is not null
                    && Directory.Exists(Path.Combine(root, "Assets"))
                    && (Directory.Exists(Path.Combine(root, "ProjectSettings"))
                        || Directory.Exists(Path.Combine(root, "Packages")));
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return false;
    }

    public static bool IsExcluded(string path) =>
        GetExclusionReason(path) is not null;

    /// <summary>
    /// Returns the exclusion category for an excluded path. Directory exclusions
    /// identify the excluded subtree root; they do not claim to count every file
    /// below that root.
    /// </summary>
    public static string? GetExclusionReason(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "InvalidPath";

        var normalized = path.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);

        var segments = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        var excludedSegment = segments.FirstOrDefault(ExcludedDirectories.Contains);
        if (excludedSegment is not null)
        {
            return excludedSegment.ToLowerInvariant() switch
            {
                "generated" => "GeneratedCode",
                "thirdparty" or "third-party" or "vendor" => "VendoredCode",
                ".git" or ".vs" or ".idea" or ".vscode" or "bin" or "obj"
                    or "node_modules" or "dist" or "build" or "__pycache__"
                    or ".venv" or "venv" or "coverage" or "testresults"
                    or "logs" or "packagecache" or ".nuget" => "BuildOrDependencyCache",
                _ => "ExcludedDirectory"
            };
        }

        // Unity TextMesh Pro example projects are vendored samples, not application
        // architecture. Exclude only that known combination rather than every
        // first-party directory named "TextMeshPro" or "Examples & Extras".
        var hasTextMeshProDirectory = segments.Any(segment =>
            segment.Equals("TextMesh Pro", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("TextMeshPro", StringComparison.OrdinalIgnoreCase));
        var hasExamplesDirectory = segments.Any(segment =>
            segment.Equals("Examples & Extras", StringComparison.OrdinalIgnoreCase));

        if (hasTextMeshProDirectory && hasExamplesDirectory)
            return "VendoredExample";

        if (IsUnityTempPath(path))
            return "UnityGeneratedCache";

        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            && fileName.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
        {
            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (projectDirectory is not null
                && Directory.Exists(Path.Combine(projectDirectory, "Assets"))
                && (Directory.Exists(Path.Combine(projectDirectory, "ProjectSettings"))
                    || Directory.Exists(Path.Combine(projectDirectory, "Packages"))))
            {
                return "UnityGeneratedProjectFile";
            }
        }

        if (fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "GeneratedFile";
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
                    return "UnityGeneratedCache";
                }
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }
}
