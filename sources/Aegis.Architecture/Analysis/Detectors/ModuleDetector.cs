using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class ModuleDetector
{
    private static readonly string[] ProjectFilePatterns =
    {
        "*.csproj",
        "package.json",
        "pom.xml"
    };


    public static void Analyze(
        ProjectArchitectureContext ctx,
        List<string> files)
    {
        var candidateDirs = files
            .Select(Path.GetDirectoryName)
            .Where(x => x != null)
            .Select(x => x!)
            .Distinct()
            .ToList();


        foreach (var path in candidateDirs)
        {
            var projectFile =
                FindProjectFile(path);

            if (projectFile is null)
            {
                continue;
            }


            var module = new ProjectModuleContext
            {
                // Real project/module name taken from the actual project file
                // (e.g. "Aegis.Architecture" from "Aegis.Architecture.csproj"), not a folder-name guess.
                // Folder names and project names can diverge, and this is the name that
                // actually identifies the module to anything consuming this context —
                // including the layer detector, which now uses this name directly.
                Name = Path.GetFileNameWithoutExtension(projectFile),
                Path = path,
                Type = DetectModuleType(path)
            };


            module.Languages =
                files
                .Where(f => f.StartsWith(path))
                .Select(GetLanguage)
                .Distinct()
                .ToList();


            ctx.Modules.Add(module);
        }

        // Unity projects frequently have no committed .csproj file. Discover
        // them from Unity's Assets + ProjectSettings/Packages layout so their
        // scripts receive a project scope instead of being lost in a mixed scan.
        var unityRoot = FindUnityProjectRoot(ctx.RootPath, files);
        if (!string.IsNullOrWhiteSpace(unityRoot))
        {
            var existing = ctx.Modules.FirstOrDefault(module =>
                string.Equals(
                    Path.GetFullPath(module.Path),
                    Path.GetFullPath(unityRoot),
                    StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.Type = "Unity";
            }
            else
            {
                ctx.Modules.Add(new ProjectModuleContext
                {
                    Name = Path.GetFileName(
                        unityRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    Path = unityRoot,
                    Type = "Unity",
                    Languages = files
                        .Where(file => IsSameOrDescendantPath(file, unityRoot))
                        .Select(GetLanguage)
                        .Where(language => !language.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                });
            }
        }
    }

    private static string? FindUnityProjectRoot(
        string rootPath,
        IReadOnlyCollection<string> files)
    {
        var root = Path.GetFullPath(rootPath);
        var markerFiles = files.Where(file =>
            file.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
            || (Path.GetFileName(file).Equals("ProjectVersion.txt", StringComparison.OrdinalIgnoreCase)
                && file.Contains(
                    $"{Path.DirectorySeparatorChar}ProjectSettings{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)));

        foreach (var marker in markerFiles)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(marker));
            while (!string.IsNullOrWhiteSpace(directory)
                && IsSameOrDescendantPath(directory, root))
            {
                if (Directory.Exists(Path.Combine(directory, "Assets"))
                    && (Directory.Exists(Path.Combine(directory, "ProjectSettings"))
                        || Directory.Exists(Path.Combine(directory, "Packages"))))
                {
                    return directory;
                }

                if (string.Equals(
                    directory.TrimEnd(Path.DirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                    break;

                directory = Directory.GetParent(directory)?.FullName;
            }
        }

        return null;
    }

    private static bool IsSameOrDescendantPath(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// Returns the first recognized project/manifest file in the given directory, or null
    /// if none exists.
    ///
    /// File.Exists does not support wildcards — the previous implementation called
    /// File.Exists(Path.Combine(path, "*.csproj")), which checks for a literal file named
    /// "*.csproj" and therefore never matched anything. That meant no C#/.NET project was
    /// ever detected as a module root, so ctx.Modules stayed empty for any .NET-only
    /// codebase and every violation fell through to "Unclassified" regardless of keyword
    /// logic downstream. Directory.GetFiles supports the wildcard correctly.
    /// </summary>
    private static string? FindProjectFile(
        string path)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }


        foreach (var pattern in ProjectFilePatterns)
        {
            var matches =
                Directory.GetFiles(path, pattern);

            if (matches.Length > 0)
            {
                return matches[0];
            }
        }


        return null;
    }


    private static string GetLanguage(string file)
    {
        if (file.EndsWith(".cs"))
            return "C#";

        if (file.EndsWith(".java"))
            return "Java";

        if (file.EndsWith(".py"))
            return "Python";

        if (file.EndsWith(".ts"))
            return "TypeScript";

        return "Unknown";
    }


    /// <summary>
    /// Auxiliary, best-effort classification kept on ProjectModuleContext.Type for
    /// informational purposes. No longer used to derive the Layer name — see
    /// LayerDetector, which now uses the real project name directly instead of guessing
    /// a semantic bucket from this keyword match.
    /// </summary>
    private static string DetectModuleType(string path)
    {
        var name = path.ToLowerInvariant();


        if (name.Contains("api"))
            return "Api";

        if (name.Contains("application"))
            return "Application";

        if (name.Contains("domain"))
            return "Domain";

        if (name.Contains("infra"))
            return "Infrastructure";

        if (name.Contains("test"))
            return "Test";

        return "Unknown";
    }
}