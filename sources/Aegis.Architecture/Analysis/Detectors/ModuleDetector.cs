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
        "pom.xml",
        "build.gradle",
        "build.gradle.kts",
        "settings.gradle",
        "settings.gradle.kts"
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
        var projectPaths = manifests
            .Select(group => group.First().Directory)
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
                BuildSystem = ResolveBuildSystem(projectFiles),
                Type = DetectModuleType(context.RootPath, modulePath, moduleName)
            };

            var dotNetProject = projectFiles.FirstOrDefault(file =>
                file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
            if (dotNetProject is not null)
                PopulateDotNetMetadata(module, dotNetProject, files, projectPaths);

            var packageManifest = projectFiles.FirstOrDefault(file =>
                Path.GetFileName(file).Equals("package.json", StringComparison.OrdinalIgnoreCase));
            if (packageManifest is not null)
                PopulateNodeMetadata(module, packageManifest);

            if (module.BuildSystem == Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Maven)
            {
                var pomManifest = projectFiles.FirstOrDefault(
                    LanguageAndBuildDetector.IsMavenManifest);
                if (pomManifest is not null)
                    PopulateMavenMetadata(module, pomManifest);
            }
            else if (module.BuildSystem == Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Gradle)
            {
                PopulateGradleMetadata(module);
            }

            if (module.IsTestProject
                || string.Equals(
                    module.Type,
                    "Test",
                    StringComparison.OrdinalIgnoreCase))
            {
                module.IsTestProject = true;
                module.Type = "Test";
                module.ProjectKind = "Test";
                module.IsLibrary = false;
            }
            else if (string.Equals(
                         module.Type,
                         "TestSupport",
                         StringComparison.OrdinalIgnoreCase))
            {
                module.IsTestSupport = true;
                module.ProjectKind = "TestSupport";
            }

            // Preserve a project-level aggregate when multiple concrete
            // technologies coexist. File-level evaluators resolve the concrete
            // framework from Frameworks and the file extension.
            module.Framework = module.Frameworks.Count switch
            {
                0 => null,
                1 => module.Frameworks[0],
                _ => "Multi-Framework"
            };

            context.Modules.Add(module);
        }

        var unityRoot = FindUnityProjectRoot(context.RootPath, files);
        if (!string.IsNullOrWhiteSpace(unityRoot))
        {
            var unityModule = context.Modules.FirstOrDefault(module =>
                SamePath(module.Path, unityRoot));

            if (unityModule is null)
            {
                unityModule = new ProjectModuleContext
                {
                    Name = Path.GetFileName(
                        unityRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    Path = unityRoot
                };
                context.Modules.Add(unityModule);
            }

            unityModule.Type = "Unity";
            unityModule.ProjectKind = "UnityProject";
            unityModule.Framework = "Unity";
            unityModule.Frameworks = ["Unity"];
            unityModule.Languages = files
                .Where(file => IsSameOrDescendantPath(file, unityRoot))
                .Select(GetLanguage)
                .Where(language => language != "Unknown")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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
        IReadOnlyCollection<string> files,
        IReadOnlyCollection<string> projectPaths)
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
                IsOwnedByProject(file, module.Path, projectPaths)
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

    private static bool IsOwnedByProject(
        string file,
        string projectPath,
        IReadOnlyCollection<string> projectPaths)
    {
        var owner = projectPaths
            .Where(path => IsSameOrDescendantPath(file, path))
            .OrderByDescending(path => NormalizePath(path).Length)
            .FirstOrDefault();

        return owner is not null && SamePath(owner, projectPath);
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

    private static void PopulateGradleMetadata(ProjectModuleContext module)
    {
        module.ProjectKind = "JavaProject";
        module.Framework = "Java";
        AddFramework(module, "Java");
    }

    private static Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem ResolveBuildSystem(
        IReadOnlyCollection<string> projectFiles)
    {
        var buildSystems = new List<Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem>();

        if (projectFiles.Any(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
            buildSystems.Add(Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.DotNet);
        if (projectFiles.Any(LanguageAndBuildDetector.IsMavenManifest))
            buildSystems.Add(Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Maven);
        if (projectFiles.Any(LanguageAndBuildDetector.IsGradleManifest))
            buildSystems.Add(Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Gradle);
        if (projectFiles.Any(file =>
                Path.GetFileName(file).Equals("package.json", StringComparison.OrdinalIgnoreCase)))
            buildSystems.Add(Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Npm);

        return buildSystems.Count == 1
            ? buildSystems[0]
            : Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Unknown;
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
        if (name.Equals("settings.gradle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("settings.gradle.kts", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (name.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase))
            return 3;
        return 4;
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

        if (LanguageAndBuildDetector.IsGradleManifest(projectFile))
        {
            return Path.GetFileName(modulePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
        }

        return Path.GetFileNameWithoutExtension(projectFile);
    }

    private static string DetectModuleType(
        string rootPath,
        string path,
        string moduleName)
    {
        // A project nested under an explicit test directory within the scanned
        // repository is test scope even when its name or build metadata omits it.
        var relativePath = Path.GetRelativePath(
            Path.GetFullPath(rootPath),
            Path.GetFullPath(path));

        if (relativePath
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("test", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("tests", StringComparison.OrdinalIgnoreCase)))
        {
            return "Test";
        }

        var pathName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(path)).ToLowerInvariant();

        if (pathName.Contains("testsupport", StringComparison.Ordinal)
            || pathName.Equals("testing", StringComparison.Ordinal)
            || pathName.EndsWith(".testing", StringComparison.Ordinal))
        {
            return "TestSupport";
        }

        var pathType = DetectModuleTypeFromName(pathName);
        return pathType != "Unknown"
            ? pathType
            : DetectModuleTypeFromName(moduleName.ToLowerInvariant());
    }

    private static string DetectModuleTypeFromName(string name)
    {
        if (name is "test" or "tests"
            || name.EndsWith(".test", StringComparison.Ordinal)
            || name.EndsWith(".tests", StringComparison.Ordinal)
            || name.EndsWith("tests", StringComparison.Ordinal)
            || name.StartsWith("test-", StringComparison.Ordinal)
            || name.StartsWith("tests-", StringComparison.Ordinal))
        {
            return "Test";
        }

        if (name.Equals("testing", StringComparison.Ordinal)
            || name.EndsWith(".testing", StringComparison.Ordinal)
            || name.Contains("testsupport", StringComparison.Ordinal))
        {
            return "TestSupport";
        }
        // Explicit contract packages take precedence over the technical family
        // names used by adapters such as Http.Contracts.
        if (name.Contains("contracts", StringComparison.Ordinal)
            || name.Contains("annotations", StringComparison.Ordinal)
            || (name.EndsWith(".headers", StringComparison.Ordinal)
                && !name.Split('.').Contains("http", StringComparer.Ordinal))
            || (name.EndsWith(".errors", StringComparison.Ordinal)
                && !name.Split('.').Contains("http", StringComparer.Ordinal)))
            return "Contracts";

        if (name.EndsWith(".api", StringComparison.Ordinal)
            || name.EndsWith("api", StringComparison.Ordinal))
            return "Api";
        if (name.Contains("application", StringComparison.Ordinal)
            || name.EndsWith(".app", StringComparison.Ordinal))
            return "Application";
        if (name.Contains("domain", StringComparison.Ordinal)
            || name.Contains("business", StringComparison.Ordinal)
            || name.EndsWith(".core", StringComparison.Ordinal))
            return "Domain";
        // Foundational runtime capabilities and platform adapters belong to
        // infrastructure even when their package names omit "Infrastructure".
        if (name.Contains("infrastructure", StringComparison.Ordinal)
            || name.Contains("persistence", StringComparison.Ordinal)
            || name.Contains("database", StringComparison.Ordinal)
            || name.Contains("mediator", StringComparison.Ordinal)
            || name.Contains("messaging", StringComparison.Ordinal)
            || name.Contains("http", StringComparison.Ordinal)
            || name.Contains("refit", StringComparison.Ordinal)
            || name.Contains("serialization", StringComparison.Ordinal)
            || name.Contains("serializer", StringComparison.Ordinal)
            || name.Contains("reflection", StringComparison.Ordinal)
            || name.Contains("dependencyinjection", StringComparison.Ordinal)
            || name.Contains("bootstrap", StringComparison.Ordinal)
            || name.Contains("hosting", StringComparison.Ordinal)
            || name.Contains("entityframework", StringComparison.Ordinal)
            || name.Contains("cosmos", StringComparison.Ordinal)
            || name.Contains("mongodb", StringComparison.Ordinal)
            || name.Contains("grpc", StringComparison.Ordinal)
            || name.Contains("aras", StringComparison.Ordinal)
            || name.Contains("mapping", StringComparison.Ordinal)
            || name.Split('.').Contains("io", StringComparer.Ordinal)
            || name.Contains("opentelemetry", StringComparison.Ordinal)
            || name.Contains("polly", StringComparison.Ordinal)
            || name.Contains("kafka", StringComparison.Ordinal)
            || name.Contains("rabbitmq", StringComparison.Ordinal)
            || name.Contains("masstransit", StringComparison.Ordinal)
            || name.Contains("sagas", StringComparison.Ordinal)
            || name.Contains("azureevent", StringComparison.Ordinal)
            || name.Contains("multitenancy", StringComparison.Ordinal)
            || name.Contains("identity", StringComparison.Ordinal)
            || name.Contains("sso", StringComparison.Ordinal)
            || name.Equals("franz.common", StringComparison.Ordinal)
            || name.Contains("caching", StringComparison.Ordinal)
            || name.Contains("cache", StringComparison.Ordinal)
            || name.Contains("logging", StringComparison.Ordinal)
            || name.Contains("security", StringComparison.Ordinal)
            || name.Contains("integration", StringComparison.Ordinal)
            || name.Contains("transport", StringComparison.Ordinal)
            || name.Contains("storage", StringComparison.Ordinal)
            || name.Contains("observability", StringComparison.Ordinal)
            || name.Contains("resilience", StringComparison.Ordinal)
            || name.Contains("abstractions", StringComparison.Ordinal)
            || name.EndsWith(".base", StringComparison.Ordinal)
            || name.StartsWith("base.", StringComparison.Ordinal))
            return "Infrastructure";
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
