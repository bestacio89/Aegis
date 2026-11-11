using System.Text.RegularExpressions;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Reads the statically declarative subset of Gradle settings files. It does not
/// evaluate Groovy/Kotlin expressions or execute Gradle.
/// </summary>
internal static class GradleProjectModelResolver
{
    private static readonly Regex RootProjectNameExpression = new(
        @"\brootProject\s*\.\s*name\s*=\s*['""](?<value>[^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IncludeExpression = new(
        @"\binclude\s*(?:\((?<parenthesized>[^)]*)\)|(?<groovy>(?:['""][^'""]+['""]\s*,?\s*)+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProjectDirectoryExpression = new(
        @"\bproject\s*\(\s*['""](?<project>[^'""]+)['""]\s*\)\s*\.\s*projectDir\s*=\s*file\s*\(\s*['""](?<directory>[^'""]+)['""]\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IncludedBuildExpression = new(
        @"\bincludeBuild\s*\(?\s*['""](?<directory>[^'""]+)['""]\s*\)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex QuotedValueExpression = new(
        @"['""](?<value>(?:\\.|[^'""])*)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static void Analyze(
        ProjectArchitectureContext context,
        IReadOnlyCollection<string> files)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(files);

        var settingsFiles = files
            .Where(IsSettingsManifest)
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .ToArray();

        foreach (var settingsFile in settingsFiles)
        {
            var rootDirectory = Path.GetDirectoryName(settingsFile)!;
            string source;
            try
            {
                source = StripComments(File.ReadAllText(settingsFile));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                var failedModel = new GradleProjectModel
                {
                    SettingsFile = settingsFile,
                    RootDirectory = rootDirectory,
                    Diagnostics =
                    [
                        Diagnostic(
                            "GradleSettingsReadFailed",
                            $"Gradle settings could not be read: {exception.Message}",
                            settingsFile)
                    ],
                    IsComplete = false
                };
                AttachRootModel(context, rootDirectory, failedModel);
                continue;
            }

            var diagnostics = new List<AnalysisDiagnostic>();
            var rootNameMatch = RootProjectNameExpression.Match(source);
            var rootProjectName = rootNameMatch.Success
                ? rootNameMatch.Groups["value"].Value.Trim()
                : null;

            if (rootProjectName?.Contains('$') == true)
            {
                diagnostics.Add(Diagnostic(
                    "GradleDynamicRootProjectName",
                    "The root project name contains interpolation and cannot be resolved statically.",
                    settingsFile));
                rootProjectName = null;
            }

            var includedProjectPaths = ParseIncludedProjects(source, settingsFile, diagnostics);
            var projectDirectories = ParseProjectDirectories(source, settingsFile, diagnostics);
            var includedBuilds = ParseIncludedBuilds(source, rootDirectory, settingsFile, diagnostics);

            var projects = new List<GradleProjectModel.ProjectReference>
            {
                new(
                    ":",
                    rootDirectory,
                    FindBuildFile(rootDirectory, files),
                    true)
            };

            foreach (var projectPath in includedProjectPaths)
            {
                var projectDirectory = projectDirectories.TryGetValue(projectPath, out var overrideDirectory)
                    ? overrideDirectory
                    : ResolveDefaultProjectDirectory(rootDirectory, projectPath);

                if (!IsSameOrDescendantPath(projectDirectory, rootDirectory))
                {
                    diagnostics.Add(Diagnostic(
                        "GradleProjectDirectoryOutsideRoot",
                        $"Project '{projectPath}' resolves outside the Gradle build root and is not inspected.",
                        settingsFile));
                    continue;
                }

                var includedBuildOwner = includedBuilds.FirstOrDefault(includedBuild =>
                    IsSameOrDescendantPath(projectDirectory, includedBuild.Directory));
                if (includedBuildOwner is not null)
                {
                    diagnostics.Add(Diagnostic(
                        "GradleProjectOverlapsIncludedBuild",
                        $"Project '{projectPath}' resolves inside included build '{includedBuildOwner.BuildPath}' and is not attached to the declaring build.",
                        settingsFile));
                    continue;
                }

                projects.Add(new GradleProjectModel.ProjectReference(
                    projectPath,
                    projectDirectory,
                    FindBuildFile(projectDirectory, files),
                    false));
            }

            var versionCatalog = Path.Combine(rootDirectory, "gradle", "libs.versions.toml");
            var versionCatalogFiles = new List<string>();
            if (files.Any(file => PathsEqual(file, versionCatalog)))
                versionCatalogFiles.Add(Path.GetFullPath(versionCatalog));

            var model = new GradleProjectModel
            {
                SettingsFile = settingsFile,
                RootDirectory = rootDirectory,
                RootProjectName = rootProjectName,
                Projects = projects,
                IncludedBuilds = includedBuilds,
                VersionCatalogFiles = versionCatalogFiles,
                Diagnostics = diagnostics,
                IsComplete = diagnostics.Count == 0
            };

            AttachRootModel(context, rootDirectory, model);
            AttachIncludedProjects(context, model);
            ResolveIncludedBuildMembership(context, model);
        }
    }

    private static List<string> ParseIncludedProjects(
        string source,
        string settingsFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var projectPaths = new HashSet<string>(StringComparer.Ordinal);
        var matches = IncludeExpression.Matches(source);

        foreach (Match match in matches)
        {
            var arguments = match.Groups["parenthesized"].Success
                ? match.Groups["parenthesized"].Value
                : match.Groups["groovy"].Value;
            var values = QuotedValueExpression.Matches(arguments)
                .Cast<Match>()
                .Select(value => value.Groups["value"].Value.Trim())
                .ToArray();
            var remaining = QuotedValueExpression.Replace(arguments, string.Empty)
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Trim();

            if (remaining.Length > 0 || values.Any(value => value.Contains('$')))
            {
                diagnostics.Add(Diagnostic(
                    "GradleDynamicProjectInclude",
                    "A Gradle include expression contains dynamic arguments and is only partially discoverable.",
                    settingsFile));
            }

            foreach (var value in values.Where(value => value.Length > 0 && !value.Contains('$')))
            {
                var projectPath = value.StartsWith(':') ? value : $":{value.Replace('/', ':')}";
                projectPaths.Add(NormalizeProjectPath(projectPath));
            }
        }

        var includeCount = Regex.Matches(source, @"\binclude\b", RegexOptions.CultureInvariant).Count;
        if (includeCount > matches.Count)
        {
            diagnostics.Add(Diagnostic(
                "GradleDynamicProjectInclude",
                "One or more include declarations could not be interpreted as static string arguments.",
                settingsFile));
        }

        return projectPaths.OrderBy(path => path, StringComparer.Ordinal).ToList();
    }

    private static Dictionary<string, string> ParseProjectDirectories(
        string source,
        string settingsFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var directories = new Dictionary<string, string>(StringComparer.Ordinal);
        var matches = ProjectDirectoryExpression.Matches(source);

        foreach (Match match in matches)
        {
            var projectPath = NormalizeProjectPath(match.Groups["project"].Value.Trim());
            var relativeDirectory = match.Groups["directory"].Value.Trim();
            if (relativeDirectory.Contains('$'))
            {
                diagnostics.Add(Diagnostic(
                    "GradleDynamicProjectDirectory",
                    $"Project directory for '{projectPath}' contains interpolation and cannot be resolved statically.",
                    settingsFile));
                continue;
            }

            directories[projectPath] = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(settingsFile)!,
                relativeDirectory));
        }

        var declarations = Regex.Matches(
            source,
            @"\bproject\s*\([^)]*\)\s*\.\s*projectDir\s*=",
            RegexOptions.CultureInvariant).Count;
        if (declarations > matches.Count)
        {
            diagnostics.Add(Diagnostic(
                "GradleDynamicProjectDirectory",
                "One or more projectDir assignments are not static file(...) expressions.",
                settingsFile));
        }

        return directories;
    }

    private static List<GradleProjectModel.IncludedBuild> ParseIncludedBuilds(
        string source,
        string rootDirectory,
        string settingsFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var includedBuilds = new List<GradleProjectModel.IncludedBuild>();
        var matches = IncludedBuildExpression.Matches(source);

        foreach (Match match in matches)
        {
            var relativeDirectory = match.Groups["directory"].Value.Trim();
            if (relativeDirectory.Contains('$'))
            {
                diagnostics.Add(Diagnostic(
                    "GradleDynamicIncludedBuild",
                    "An included build path contains interpolation and cannot be resolved statically.",
                    settingsFile));
                continue;
            }

            var includedDirectory = Path.GetFullPath(
                Path.Combine(rootDirectory, relativeDirectory));
            if (includedBuilds.Any(includedBuild =>
                    PathsEqual(includedBuild.Directory, includedDirectory)))
            {
                continue;
            }

            includedBuilds.Add(new GradleProjectModel.IncludedBuild(
                relativeDirectory,
                includedDirectory,
                false,
                null,
                null));
        }

        var declarations = Regex.Matches(source, @"\bincludeBuild\b", RegexOptions.CultureInvariant).Count;
        if (declarations > matches.Count)
        {
            diagnostics.Add(Diagnostic(
                "GradleDynamicIncludedBuild",
                "One or more included-build declarations could not be interpreted as static paths.",
                settingsFile));
        }

        return includedBuilds;
    }

    private static void ResolveIncludedBuildMembership(
        ProjectArchitectureContext context,
        GradleProjectModel model)
    {
        var analysisRoot = Path.GetFullPath(context.RootPath);

        for (var index = 0; index < model.IncludedBuilds.Count; index++)
        {
            var includedBuild = model.IncludedBuilds[index];
            var includedDirectory = Path.GetFullPath(includedBuild.Directory);

            if (!IsSameOrDescendantPath(includedDirectory, analysisRoot))
            {
                model.Diagnostics.Add(Diagnostic(
                    "GradleIncludedBuildOutsideAnalysisRoot",
                    $"Included build '{includedBuild.BuildPath}' resolves outside the analyzed repository and is not inspected.",
                    model.SettingsFile));
                continue;
            }

            if (PathsEqual(includedDirectory, model.RootDirectory))
            {
                model.Diagnostics.Add(Diagnostic(
                    "GradleIncludedBuildSelfReference",
                    "A Gradle build cannot include its own root as a separate composite member.",
                    model.SettingsFile));
                continue;
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(includedDirectory);
            }
            catch (DirectoryNotFoundException)
            {
                model.Diagnostics.Add(Diagnostic(
                    "GradleIncludedBuildDirectoryMissing",
                    $"Included build directory '{includedBuild.BuildPath}' does not exist.",
                    model.SettingsFile));
                continue;
            }
            catch (FileNotFoundException)
            {
                model.Diagnostics.Add(Diagnostic(
                    "GradleIncludedBuildDirectoryMissing",
                    $"Included build directory '{includedBuild.BuildPath}' does not exist.",
                    model.SettingsFile));
                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                model.Diagnostics.Add(Diagnostic(
                    "GradleIncludedBuildAccessDenied",
                    $"Included build directory '{includedBuild.BuildPath}' could not be inspected: {exception.Message}",
                    model.SettingsFile));
                continue;
            }
            catch (IOException exception)
            {
                model.Diagnostics.Add(Diagnostic(
                    "GradleIncludedBuildInspectionFailed",
                    $"Included build directory '{includedBuild.BuildPath}' could not be inspected: {exception.Message}",
                    model.SettingsFile));
                continue;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                model.Diagnostics.Add(Diagnostic(
                    "GradleIncludedBuildPathNotDirectory",
                    $"Included build path '{includedBuild.BuildPath}' does not identify a directory.",
                    model.SettingsFile));
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                model.Diagnostics.Add(Diagnostic(
                    "GradleIncludedBuildReparsePointSkipped",
                    $"Included build '{includedBuild.BuildPath}' resolves through a reparse point and is not inspected.",
                    model.SettingsFile));
                continue;
            }

            var includedModule = context.Modules.FirstOrDefault(module =>
                PathsEqual(module.Path, includedDirectory)
                && module.ProjectFiles.Any(IsGradleManifest));

            if (includedModule is null)
            {
                model.Diagnostics.Add(Diagnostic(
                    "GradleIncludedBuildNotDiscovered",
                    $"Included build '{includedBuild.BuildPath}' has no independently discovered Gradle project manifest.",
                    model.SettingsFile));
                continue;
            }

            var settingsFile = includedModule.GradleModel?.SettingsFile
                ?? includedModule.ProjectFiles.FirstOrDefault(IsSettingsManifest);
            model.IncludedBuilds[index] = includedBuild with
            {
                IsDiscovered = true,
                SettingsFile = settingsFile,
                RootProjectName = includedModule.GradleModel?.RootProjectName ?? includedModule.Name
            };
        }

        model.IsComplete = model.Diagnostics.Count == 0;
    }

    private static bool IsGradleManifest(string file)
    {
        var name = Path.GetFileName(file);
        return IsSettingsManifest(file)
            || name.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase);
    }

    private static void AttachRootModel(
        ProjectArchitectureContext context,
        string rootDirectory,
        GradleProjectModel model)
    {
        var rootModule = context.Modules.FirstOrDefault(module =>
            PathsEqual(module.Path, rootDirectory));

        if (rootModule is null)
        {
            rootModule = new ProjectModuleContext
            {
                Name = model.RootProjectName ?? Path.GetFileName(rootDirectory),
                Path = rootDirectory,
                BuildSystem = ArchitectureBuildSystem.Gradle,
                ProjectKind = "GradleProject"
            };
            context.Modules.Add(rootModule);
        }

        rootModule.GradleModel = model;
        rootModule.BuildSystem = ArchitectureBuildSystem.Gradle;
        if (!string.IsNullOrWhiteSpace(model.RootProjectName))
            rootModule.Name = model.RootProjectName;
    }

    private static void AttachIncludedProjects(
        ProjectArchitectureContext context,
        GradleProjectModel model)
    {
        foreach (var project in model.Projects.Where(project => !project.IsRoot))
        {
            var module = context.Modules.FirstOrDefault(candidate =>
                PathsEqual(candidate.Path, project.ProjectDirectory));

            if (module is null)
            {
                module = new ProjectModuleContext
                {
                    Name = ProjectName(project.ProjectPath),
                    Path = project.ProjectDirectory,
                    ProjectFile = project.BuildFile,
                    ProjectFiles = project.BuildFile is null ? [] : [project.BuildFile],
                    BuildSystem = ArchitectureBuildSystem.Gradle,
                    ProjectKind = "JavaProject",
                    Framework = "Java",
                    Frameworks = ["Java"]
                };
                context.Modules.Add(module);
            }
            else
            {
                module.Name = ProjectName(project.ProjectPath);
                module.BuildSystem = ArchitectureBuildSystem.Gradle;
                if (project.BuildFile is not null
                    && !module.ProjectFiles.Contains(project.BuildFile, PathComparer))
                {
                    module.ProjectFiles.Add(project.BuildFile);
                }

                module.ProjectFile ??= project.BuildFile;
            }
        }
    }

    private static string? FindBuildFile(
        string projectDirectory,
        IReadOnlyCollection<string> files) =>
        files.FirstOrDefault(file =>
            PathsEqual(Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty, projectDirectory)
            && (Path.GetFileName(file).Equals("build.gradle", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase)));

    private static string ResolveDefaultProjectDirectory(string rootDirectory, string projectPath)
    {
        var relativePath = projectPath.Trim(':')
            .Replace(':', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
    }

    private static string NormalizeProjectPath(string projectPath)
    {
        var segments = projectPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? ":" : $":{string.Join(':', segments)}";
    }

    private static string ProjectName(string projectPath) =>
        projectPath.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
        ?? projectPath;

    private static bool IsSettingsManifest(string file)
    {
        var name = Path.GetFileName(file);
        return name.Equals("settings.gradle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("settings.gradle.kts", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripComments(string source)
    {
        var characters = source.ToCharArray();
        var inString = false;
        var escaped = false;
        var quote = '\0';
        var lineComment = false;
        var blockComment = false;

        for (var index = 0; index < characters.Length; index++)
        {
            var current = characters[index];
            var next = index + 1 < characters.Length ? characters[index + 1] : '\0';

            if (lineComment)
            {
                if (current == '\n')
                    lineComment = false;
                else
                    characters[index] = ' ';
                continue;
            }

            if (blockComment)
            {
                if (current == '*' && next == '/')
                {
                    characters[index] = ' ';
                    characters[index + 1] = ' ';
                    index++;
                    blockComment = false;
                }
                else if (current != '\n' && current != '\r')
                {
                    characters[index] = ' ';
                }

                continue;
            }

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == quote)
                    inString = false;

                continue;
            }

            if (current is '\'' or '"')
            {
                inString = true;
                quote = current;
                continue;
            }

            if (current == '/' && next == '/')
            {
                characters[index] = ' ';
                characters[index + 1] = ' ';
                index++;
                lineComment = true;
            }
            else if (current == '/' && next == '*')
            {
                characters[index] = ' ';
                characters[index + 1] = ' ';
                index++;
                blockComment = true;
            }
        }

        return new string(characters);
    }

    private static bool IsSameOrDescendantPath(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            PathComparison);

    private static AnalysisDiagnostic Diagnostic(string code, string message, string target) =>
        new()
        {
            Code = code,
            Component = nameof(GradleProjectModelResolver),
            Message = message,
            Target = target,
            AffectsCoverage = true
        };
}
