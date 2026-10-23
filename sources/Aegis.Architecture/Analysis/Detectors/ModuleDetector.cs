using System.Text.Json;
using System.Xml.Linq;

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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();


        foreach (var path in candidateDirs)
        {
            var projectFile =
                FindProjectFile(path);

            if (projectFile is null)
            {
                continue;
            }


            var moduleName = ResolveModuleName(projectFile, path);
            var module = new ProjectModuleContext
            {
                // Project files and package manifests use different identity fields.
                // Path.GetFileNameWithoutExtension("package.json") and ("pom.xml")
                // produce generic names, not the actual module identity.
                Name = moduleName,
                Path = path,
                Type = DetectModuleType(path, moduleName)
            };


            module.Languages =
                files
                .Where(file => IsSameOrDescendantPath(file, path))
                .Select(GetLanguage)
                .Where(language => !language.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
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


    private static string ResolveModuleName(string projectFile, string modulePath)
    {
        var fileName = Path.GetFileName(projectFile);
        var directoryName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(modulePath));

        if (fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(projectFile));
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    return name.GetString()!.Trim();
                }
            }
            catch (JsonException)
            {
                // Keep discovery resilient; the manifest evaluator reports malformed JSON.
            }
            catch (IOException)
            {
                // The unreadable manifest remains a discovery limitation, not a fake module name.
            }
            catch (UnauthorizedAccessException)
            {
                // The unreadable manifest remains a discovery limitation, not a fake module name.
            }

            return directoryName;
        }

        if (fileName.Equals("pom.xml", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var document = XDocument.Load(projectFile);
                var artifactId = document
                    .Descendants()
                    .FirstOrDefault(element =>
                        element.Name.LocalName.Equals("artifactId", StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                if (!string.IsNullOrWhiteSpace(artifactId))
                    return artifactId.Trim();
            }
            catch (System.Xml.XmlException)
            {
                // Keep discovery resilient; configuration diagnostics own parse failures.
            }
            catch (IOException)
            {
                // The unreadable manifest remains a discovery limitation.
            }
            catch (UnauthorizedAccessException)
            {
                // The unreadable manifest remains a discovery limitation.
            }

            return directoryName;
        }

        return Path.GetFileNameWithoutExtension(projectFile);
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
    private static string DetectModuleType(string path, string moduleName)
    {
        var pathName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(path)).ToLowerInvariant();

        // The directory is the strongest local clue. When it is generic (for
        // example, "apps/ui" or "packages/foo"), use the actual project or
        // manifest identity as a fallback instead of leaving the module
        // unclassified merely because its folder does not encode its role.
        var pathType = DetectModuleTypeFromName(pathName);
        return pathType != "Unknown"
            ? pathType
            : DetectModuleTypeFromName(moduleName.ToLowerInvariant());
    }

    private static string DetectModuleTypeFromName(string name)
    {
        // Keep classification scoped to a module identity, never the full path.
        if (name.Contains("test", StringComparison.Ordinal))
            return "Test";

        if (name.EndsWith(".api", StringComparison.Ordinal)
            || name.EndsWith("api", StringComparison.Ordinal))
            return "Api";

        if (name.Contains("application", StringComparison.Ordinal)
            || name.EndsWith(".app", StringComparison.Ordinal))
            return "Application";

        if (name.Contains("domain", StringComparison.Ordinal)
            || name.EndsWith(".core", StringComparison.Ordinal))
            return "Domain";

        if (name.Contains("infrastructure", StringComparison.Ordinal)
            || name.Contains("persistence", StringComparison.Ordinal)
            || name.Contains("database", StringComparison.Ordinal))
            return "Infrastructure";

        if (name.Contains("contracts", StringComparison.Ordinal)
            || name.Contains("abstractions", StringComparison.Ordinal))
            return "Contracts";

        if (name.Contains("consumer", StringComparison.Ordinal)
            || name.Contains("worker", StringComparison.Ordinal)
            || name.Contains("background", StringComparison.Ordinal))
            return "Consumer";

        if (name.Contains("admin", StringComparison.Ordinal)
            || name.Contains("presentation", StringComparison.Ordinal)
            || name.Contains("web", StringComparison.Ordinal))
            return "Presentation";

        if (name.Contains("client", StringComparison.Ordinal))
            return "Client";

        return "Unknown";
    }
}