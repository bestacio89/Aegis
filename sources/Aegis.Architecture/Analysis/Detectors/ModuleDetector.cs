using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Discovers project boundaries and records project-local technology metadata.
/// Repository-level context remains an aggregate; it is not used as a substitute
/// for the identity of the project that owns an individual source file.
/// </summary>
internal static class ModuleDetector
{
    private static readonly string[] ProjectManifestNames =
    [
        "package.json",
        "pom.xml"
    ];

    public static void Analyze(
        ProjectArchitectureContext context,
        List<string> files)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(files);

        context.Modules.Clear();

        var manifests = files
            .Where(IsProjectManifest)
            .Select(file => new
            {
                File = file,
                Directory = Path.GetDirectoryName(Path.GetFullPath(file))!
            })
            .GroupBy(item => NormalizePath(item.Directory), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key.Length)
            .ToArray();

        foreach (var manifestGroup in manifests)
        {
            var projectFiles = manifestGroup
                .Select(item => item.File)
                .OrderBy(GetManifestPriority)
                .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var modulePath = manifestGroup.First().Directory;
            var primaryProjectFile = projectFiles[0];
            var moduleName = ResolveModuleName(primaryProjectFile, modulePath);

            var module = new ProjectModuleContext
            {
                Name = moduleName,
                Path = modulePath,
                ProjectFile = primaryProjectFile,
                ProjectFiles = projectFiles,
                Type = DetectModuleType(modulePath, moduleName)
            };

            var dotNetProject = projectFiles.FirstOrDefault(file =>
                file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
            if (dotNetProject is not null)
                PopulateDotNetMetadata(module, dotNetProject, files);

            var packageManifest = projectFiles.FirstOrDefault(file =>
                Path.GetFileName(file).Equals("package.json", StringComparison.OrdinalIgnoreCase));
            if (packageManifest is not null)
                PopulateNodeMetadata(module, packageManifest);

            var pomManifest = projectFiles.FirstOrDefault(file =>
                Path.GetFileName(file).Equals("pom.xml", StringComparison.OrdinalIgnoreCase));
            if (pomManifest is not null)
                PopulateMavenMetadata(module, pomManifest);

            if (module.IsTestProject)
                module.Type = "Test";

            module.Framework = module.Frameworks.Count switch
            {
                0 => null,
                1 => module.Frameworks[0],
                _ => "Multi-Framework"
            };

            context.Modules.Add(module);
        }

        var unityRoot = FindUnityProjectRoot(context.RootPath, files);
        if (!string.IsNullOrWhiteSpace(unityRoot)
            && !context.Modules.Any(module => SamePath(module.Path, unityRoot)))
        {
            context.Modules.Add(new ProjectModuleContext
            {
                Name = Path.GetFileName(
                    unityRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Path = unityRoot,
                Type = "Unity",
                ProjectKind = "UnityProject",
                Framework = "Unity",
                Frameworks = ["Unity"],
                Languages = files
                    .Where(file => IsSameOrDescendantPath(file, unityRoot))
                    .Select(GetLanguage)
                    .Where(language => language != "Unknown")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        PopulateOwnedLanguages(context.Modules, files);
    }

    private static void PopulateOwnedLanguages(
        IReadOnlyCollection<ProjectModuleContext> modules,
        IReadOnlyCollection<string> files)
    {
        var orderedModules = modules
            .OrderByDescending(module => NormalizePath(module.Path).Length)
            .ToArray();

        foreach (var module in modules)
        {
            var ownedFiles = files.Where(file =>
            {
                if (!IsSameOrDescendantPath(file, module.Path))
                    return false;

                var owner = orderedModules.FirstOrDefault(candidate =>
                    IsSameOrDescendantPath(file, candidate.Path));

                return ReferenceEquals(owner, module);
            });

            module.Languages = ownedFiles
                .Select(GetLanguage)
                .Where(language => language != "Unknown")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(language => language, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static void PopulateDotNetMetadata(
        ProjectModuleContext module,
        string projectFile,
        IReadOnlyCollection<string> files)
    {
        try
        {
            var document = XDocument.Load(projectFile);
            var root = document.Root;
            var sdk = root?.Attribute("Sdk")?.Value ?? string.Empty;
            var properties = document.Descendants()
                .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
                .GroupBy(element => element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);

            var packageReferences = document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element =>
                    element.Attribute("Include")?.Value
                    ?? element.Attribute("Update")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            module.TargetFrameworks = ReadTargetFrameworks(properties);
            module.IsTestProject =
                IsTrue(properties, "IsTestProject")
                || packageReferences.Any(IsTestPackage);
            module.IsWebProject =
                sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
                || packageReferences.Any(package =>
                    package.Contains("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
            var isWpf = IsTrue(properties, "UseWPF");
            var isWindowsForms = IsTrue(properties, "UseWindowsForms");
            var outputType = properties.GetValueOrDefault("OutputType", "Library");

            module.IsExecutable = outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase)
                || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase);
            module.IsLibrary = !module.IsTestProject
                && !module.IsWebProject
                && !module.IsExecutable;

            var hasBlazorComponents = files.Any(file =>
                IsSameOrDescendantPath(file, module.Path)
                && Path.GetExtension(file).Equals(".razor", StringComparison.OrdinalIgnoreCase));
            var hasEntityFramework = packageReferences.Any(package =>
                package.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));

            module.Framework = module.IsWebProject
                ? "ASP.NET Core"
                : isWpf
                    ? "WPF"
                    : isWindowsForms
                        ? "Windows Forms"
                        : hasEntityFramework
                            ? ".NET (EF Core)"
                            : ".NET";

            AddFramework(module, module.Framework);
            if (hasBlazorComponents)
                AddFramework(module, "Blazor");

            module.ProjectKind = module.IsTestProject
                ? "Test"
                : module.IsWebProject
                    ? "WebApplication"
                    : isWpf || isWindowsForms
                        ? "DesktopApplication"
                        : module.IsExecutable
                            ? "Executable"
                            : "ClassLibrary";
        }
        catch (Exception exception) when (
            exception is System.Xml.XmlException
                or IOException
                or UnauthorizedAccessException)
        {
            module.ProjectKind = "Unknown";
        }
    }

    private static List<string> ReadTargetFrameworks(
        IReadOnlyDictionary<string, string> properties)
    {
        var value = properties.GetValueOrDefault("TargetFrameworks")
            ?? properties.GetValueOrDefault("TargetFramework");

        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private static bool IsTestPackage(string package) =>
        package.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("NUnit", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("MSTest.", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrue(
        IReadOnlyDictionary<string, string> properties,
        string key) =>
        properties.TryGetValue(key, out var value)
        && bool.TryParse(value, out var parsed)
        && parsed;

    private static void PopulateNodeMetadata(
        ProjectModuleContext module,
        string packageFile)
    {
        try
        {
            var package = JsonNode.Parse(File.ReadAllText(packageFile))?.AsObject();
            var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in new[]
                     {
                         "dependencies",
                         "devDependencies",
                         "peerDependencies",
                         "optionalDependencies"
                     })
            {
                if (package?[key] is not JsonObject group)
                    continue;

                foreach (var dependency in group)
                    dependencies.Add(dependency.Key);
            }

            if (dependencies.Contains("@angular/core")
                || dependencies.Contains("@angular/cli"))
                AddFramework(module, "Angular");
            if (dependencies.Contains("react"))
                AddFramework(module, "React");
            if (dependencies.Contains("vue"))
                AddFramework(module, "Vue");

            if (module.Frameworks.Count == 0)
                AddFramework(module, "Node.js");

            if (module.ProjectKind == "Unknown")
            {
                module.ProjectKind = module.Frameworks.Any(framework =>
                    framework is "Angular" or "React" or "Vue")
                    ? "FrontendApplication"
                    : "NodePackage";
            }

            if (module.Framework is null)
            {
                module.Framework = module.Frameworks.Count switch
                {
                    0 => null,
                    1 => module.Frameworks[0],
                    _ => "Multi-Framework"
                };
            }

            if (string.IsNullOrWhiteSpace(module.Name)
                || module.Name.Equals("package", StringComparison.OrdinalIgnoreCase))
            {
                var name = package?["name"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(name))
                    module.Name = name.Trim();
            }
        }
        catch (Exception exception) when (
            exception is JsonException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            module.ProjectKind = module.ProjectKind == "Unknown"
                ? "Unknown"
                : module.ProjectKind;
        }
    }

    private static void PopulateMavenMetadata(
        ProjectModuleContext module,
        string pomFile)
    {
        try
        {
            var document = XDocument.Load(pomFile);
            var artifactId = document.Root?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "artifactId")
                ?.Value;

            if (!string.IsNullOrWhiteSpace(artifactId))
                module.Name = artifactId.Trim();

            var packaging = document.Root?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "packaging")
                ?.Value;

            module.ProjectKind = string.Equals(
                packaging,
                "war",
                StringComparison.OrdinalIgnoreCase)
                ? "WebApplication"
                : "JavaProject";
            module.Framework = "Java";
            AddFramework(module, "Java");
        }
        catch (Exception exception) when (
            exception is System.Xml.XmlException
                or IOException
                or UnauthorizedAccessException)
        {
            module.ProjectKind = "Unknown";
        }
    }

    private static void AddFramework(ProjectModuleContext module, string framework)
    {
        if (!module.Frameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
            module.Frameworks.Add(framework);
    }

    private static bool IsProjectManifest(string file)
    {
        var name = Path.GetFileName(file);
        return name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || ProjectManifestNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static int GetManifestPriority(string file)
    {
        var name = Path.GetFileName(file);
        if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }

    private static string ResolveModuleName(string projectFile, string modulePath)
    {
        if (Path.GetFileName(projectFile).Equals(
                "package.json",
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(projectFile));
                if (document.RootElement.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(name.GetString()))
                    return name.GetString()!.Trim();
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // The manifest evaluator owns malformed or unreadable package diagnostics.
            }
        }

        if (Path.GetFileName(projectFile).Equals("pom.xml", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var document = XDocument.Load(projectFile);
                var artifactId = document.Root?
                    .Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "artifactId")
                    ?.Value;
                if (!string.IsNullOrWhiteSpace(artifactId))
                    return artifactId.Trim();
            }
            catch (Exception exception) when (
                exception is System.Xml.XmlException or IOException or UnauthorizedAccessException)
            {
                // The manifest evaluator owns malformed or unreadable Maven diagnostics.
            }
        }

        return Path.GetFileNameWithoutExtension(projectFile);
    }

    private static string DetectModuleType(string path, string moduleName)
    {
        var pathName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(path)).ToLowerInvariant();

        var pathType = DetectModuleTypeFromName(pathName);
        return pathType != "Unknown"
            ? pathType
            : DetectModuleTypeFromName(moduleName.ToLowerInvariant());
    }

    private static string DetectModuleTypeFromName(string name)
    {
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
                    return directory;

                if (SamePath(directory, root))
                    break;

                directory = Directory.GetParent(directory)?.FullName;
            }
        }

        return null;
    }

    private static string GetLanguage(string file)
    {
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            ".ts" or ".tsx" => "TypeScript",
            ".js" or ".jsx" => "JavaScript",
            _ => "Unknown"
        };
    }

    private static bool IsSameOrDescendantPath(string path, string root)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(root);

        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
