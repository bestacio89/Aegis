using System.Text.RegularExpressions;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Extracts a deliberately bounded, literal subset of Gradle build declarations.
/// Gradle scripts, plugins, init scripts and dependency resolution are never executed.
/// </summary>
internal static class GradleBuildModelResolver
{
    private static readonly Regex PluginDeclaration = new(
        @"^\s*id\s*(?:\(\s*['""](?<kotlin>[^'""]+)['""]\s*\)|['""](?<groovy>[^'""]+)['""])\s*(?:version\s*['""](?<version>[^'""]+)['""])?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PluginAlias = new(
        @"^\s*alias\s*\(\s*libs\.plugins\.(?<alias>[A-Za-z0-9_.-]+)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DependencyDeclaration = new(
        @"^\s*(?<configuration>[A-Za-z][A-Za-z0-9_]*)\s*(?:\(\s*(?<argument>.+?)\s*\)|\s+(?<groovy>.+?))\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex QuotedNotation = new(
        @"^['""](?<notation>[^'""]+)['""]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CatalogAlias = new(
        @"^libs\.(?<alias>[A-Za-z0-9_.-]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProjectNotation = new(
        @"^project\s*\(\s*(?:path\s*=\s*)?['""](?<path>:[^'""]*)['""]\s*\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WrappedNotation = new(
        @"^(?<wrapper>platform|enforcedPlatform)\s*\(\s*(?<inner>.+)\s*\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ToolchainVersion = new(
        @"JavaLanguageVersion\.of\s*\(\s*(?<version>[0-9]+)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CatalogAssignment = new(
        @"^\s*(?<alias>[A-Za-z0-9_.-]+)\s*=\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CatalogModule = new(
        @"module\s*=\s*['""](?<module>[^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CatalogVersion = new(
        @"version\s*=\s*['""](?<version>[^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CatalogVersionReference = new(
        @"version\.ref\s*=\s*['""](?<reference>[^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static void Analyze(ProjectArchitectureContext context, IReadOnlyCollection<string> files)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(files);

        var gradleRoots = context.Modules
            .Where(module => module.GradleModel is not null)
            .OrderByDescending(module => Path.GetFullPath(module.Path).Length)
            .ToArray();

        context.DependencyEvidence.RemoveAll(evidence =>
            evidence.Ecosystem.Equals("Gradle", StringComparison.OrdinalIgnoreCase));

        foreach (var module in context.Modules.Where(module =>
                     module.BuildSystem == ArchitectureBuildSystem.Gradle))
        {
            var owningRoot = gradleRoots.FirstOrDefault(root =>
                IsSameOrDescendantPath(module.Path, root.Path));
            var catalogFiles = owningRoot is not null
                ? owningRoot.GradleModel!.VersionCatalogFiles
                : DiscoverLocalVersionCatalog(module.Path, files);
            var catalogDiagnostics = new List<AnalysisDiagnostic>();
            var catalog = ReadVersionCatalog(catalogFiles, catalogDiagnostics);

            var moduleBuildFile = module.ProjectFiles.FirstOrDefault(IsBuildFile)
                ?? (module.ProjectFile is not null && IsBuildFile(module.ProjectFile)
                    ? module.ProjectFile
                    : null);
            if (moduleBuildFile is null)
                continue;

            var buildFile = Path.GetFullPath(moduleBuildFile);
            string source;
            try
            {
                source = StripComments(File.ReadAllText(buildFile));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                module.GradleBuildModel = Incomplete(buildFile, "GradleBuildReadFailed",
                    $"Gradle build file could not be read: {exception.Message}");
                continue;
            }

            var diagnostics = new List<AnalysisDiagnostic>(catalogDiagnostics);
            var plugins = ParsePlugins(ExtractBlock(source, "plugins"), catalog, diagnostics, buildFile);
            var dependencies = ParseDependencies(
                ExtractBlock(source, "dependencies"), catalog, diagnostics, buildFile);
            var toolchainMatch = ToolchainVersion.Match(source);
            var toolchain = toolchainMatch.Success ? toolchainMatch.Groups["version"].Value : null;

            var sourceSets = DiscoverConventionalSourceSets(module.Path, files);
            var sourceSetBlock = ExtractBlock(source, "sourceSets");
            if (sourceSetBlock is not null && sourceSetBlock.Contains("srcDirs", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(
                    "GradleCustomSourceSetsPartiallySupported",
                    "Custom Gradle source directories are declared but are not interpreted unless they match the conventional source-set layout.",
                    buildFile));
            }

            if (Regex.IsMatch(source, @"\btoolchain\b", RegexOptions.CultureInvariant)
                && !toolchainMatch.Success)
            {
                diagnostics.Add(Diagnostic(
                    "GradleToolchainNotStaticallyResolved",
                    "A Java toolchain is declared but its language version is not a supported literal JavaLanguageVersion.of(...) expression.",
                    buildFile));
            }

            var sourceRoots = DiscoverSourceRoots(module.Path, files, sourceSets, buildFile, diagnostics);

            var model = new GradleBuildModel
            {
                BuildFile = buildFile,
                Dialect = Path.GetFileName(buildFile).EndsWith(".kts", StringComparison.OrdinalIgnoreCase)
                    ? "Kotlin"
                    : "Groovy",
                Plugins = plugins,
                Dependencies = dependencies,
                SourceSets = sourceSets,
                SourceRoots = sourceRoots,
                JavaToolchainVersion = toolchain,
                Diagnostics = diagnostics,
                IsComplete = diagnostics.Count == 0
            };
            module.GradleBuildModel = model;
            module.SourceRoots = model.SourceRoots;
            AddDependencyEvidence(context, module, model);
        }
    }

    private static List<GradleBuildModel.Plugin> ParsePlugins(
        string? block,
        VersionCatalog catalog,
        ICollection<AnalysisDiagnostic> diagnostics,
        string buildFile)
    {
        var result = new List<GradleBuildModel.Plugin>();
        if (block is null)
            return result;

        foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var alias = PluginAlias.Match(line);
            if (alias.Success)
            {
                var key = NormalizeAlias(alias.Groups["alias"].Value);
                if (catalog.Plugins.TryGetValue(key, out var plugin))
                    result.Add(new GradleBuildModel.Plugin(plugin.Id, plugin.Version, true));
                else
                    diagnostics.Add(Diagnostic("GradlePluginAliasNotResolved",
                        $"Plugin alias '{key}' is not present in the discovered version catalog.", buildFile));
                continue;
            }

            var match = PluginDeclaration.Match(line);
            if (match.Success)
            {
                var id = match.Groups["kotlin"].Success
                    ? match.Groups["kotlin"].Value
                    : match.Groups["groovy"].Value;
                result.Add(new GradleBuildModel.Plugin(
                    id,
                    match.Groups["version"].Success ? match.Groups["version"].Value : null,
                    true));
            }
            else if (line.Contains("id(", StringComparison.Ordinal)
                     || line.Contains("alias(", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic("GradlePluginDeclarationNotStatic",
                    "A plugin declaration uses an unsupported or dynamic expression.", buildFile));
            }
        }

        return result;
    }

    private static List<GradleBuildModel.Dependency> ParseDependencies(
        string? block,
        VersionCatalog catalog,
        ICollection<AnalysisDiagnostic> diagnostics,
        string buildFile)
    {
        var result = new List<GradleBuildModel.Dependency>();
        if (block is null)
            return result;

        foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = DependencyDeclaration.Match(line);
            if (!match.Success)
                continue;

            var configuration = match.Groups["configuration"].Value;
            var argument = match.Groups["argument"].Success
                ? match.Groups["argument"].Value.Trim()
                : match.Groups["groovy"].Value.Trim();
            if (configuration.Equals("constraints", StringComparison.OrdinalIgnoreCase)
                || configuration.Equals("add", StringComparison.OrdinalIgnoreCase))
                continue;

            var isAnnotationProcessor = configuration.Contains("annotationProcessor", StringComparison.OrdinalIgnoreCase)
                || configuration.Equals("kapt", StringComparison.OrdinalIgnoreCase)
                || configuration.Equals("ksp", StringComparison.OrdinalIgnoreCase);
            var wrapped = WrappedNotation.Match(argument);
            var isPlatform = wrapped.Success;
            var isEnforcedPlatform = wrapped.Success
                && wrapped.Groups["wrapper"].Value.Equals("enforcedPlatform", StringComparison.Ordinal);
            if (wrapped.Success)
                argument = wrapped.Groups["inner"].Value.Trim();

            var project = ProjectNotation.Match(argument);
            if (project.Success)
            {
                result.Add(new GradleBuildModel.Dependency(
                    configuration, null, null, null, project.Groups["path"].Value,
                    null, isPlatform, isEnforcedPlatform, isAnnotationProcessor, line.Trim()));
                continue;
            }

            var catalogAlias = CatalogAlias.Match(argument);
            if (catalogAlias.Success)
            {
                var key = NormalizeAlias(catalogAlias.Groups["alias"].Value);
                if (catalog.Libraries.TryGetValue(key, out var library))
                {
                    result.Add(new GradleBuildModel.Dependency(
                        configuration, library.Group, library.Artifact, library.Version,
                        null, key, isPlatform, isEnforcedPlatform, isAnnotationProcessor, line.Trim()));
                }
                else
                {
                    diagnostics.Add(Diagnostic("GradleDependencyAliasNotResolved",
                        $"Dependency alias '{key}' is not present in the discovered version catalog.", buildFile));
                }
                continue;
            }

            var notation = QuotedNotation.Match(argument);
            if (notation.Success)
            {
                var parts = notation.Groups["notation"].Value.Split(':');
                if (parts.Length is >= 2 and <= 3
                    && parts.All(part => part.Length > 0)
                    && parts.All(part => !part.Contains('$') && !part.Contains('{')))
                {
                    result.Add(new GradleBuildModel.Dependency(
                        configuration, parts[0], parts[1], parts.Length == 3 ? parts[2] : null,
                        null, null, isPlatform, isEnforcedPlatform, isAnnotationProcessor, line.Trim()));
                    continue;
                }

                diagnostics.Add(Diagnostic("GradleDependencyNotStaticallyResolved",
                    $"Dependency declaration '{line.Trim()}' contains an unresolved or dynamic coordinate.", buildFile));
                continue;
            }

            if (argument.Contains("libs.", StringComparison.Ordinal)
                || argument.Contains("project(", StringComparison.Ordinal)
                || argument.Contains("platform(", StringComparison.Ordinal)
                || argument.Contains("enforcedPlatform(", StringComparison.Ordinal)
                || argument.Contains("implementation(", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic("GradleDependencyNotStaticallyResolved",
                    $"Dependency declaration '{line.Trim()}' is not part of the supported literal subset.", buildFile));
            }
        }

        return result;
    }

    private static void AddDependencyEvidence(
        ProjectArchitectureContext context,
        ProjectModuleContext module,
        GradleBuildModel model)
    {
        foreach (var dependency in model.Dependencies.Where(dependency =>
                     !string.IsNullOrWhiteSpace(dependency.Group)
                     && !string.IsNullOrWhiteSpace(dependency.Artifact)))
        {
            var testOnly = dependency.Configuration.Contains("test", StringComparison.OrdinalIgnoreCase)
                || dependency.Configuration.Contains("integrationTest", StringComparison.OrdinalIgnoreCase);
            context.DependencyEvidence.Add(new ArchitectureDependencyEvidence
            {
                Id = $"gradle:{dependency.Group}:{dependency.Artifact}:{dependency.Version}:{dependency.Configuration}:{module.Path}",
                Name = $"{dependency.Group}:{dependency.Artifact}",
                Ecosystem = "Gradle",
                Kind = dependency.IsPlatform ? DependencyEvidenceKind.Platform : DependencyEvidenceKind.Package,
                DeclaredVersion = dependency.Version,
                ResolvedVersion = null,
                Scope = dependency.Configuration,
                SourceProjectPath = module.Path,
                IsDirect = true,
                IsTestOnly = testOnly,
                ResolutionStatus = string.IsNullOrWhiteSpace(dependency.Version)
                    ? DependencyResolutionStatus.PartiallyResolved
                    : DependencyResolutionStatus.Declared,
                Provenance =
                [
                    new SemanticEvidenceProvenance
                    {
                        AnalyzerId = "aegis.gradle-build-model",
                        Source = SemanticEvidenceSource.BuildModel,
                        FilePath = model.BuildFile,
                        Confidence = 1.0
                    }
                ]
            });
        }
    }

    private static IReadOnlyCollection<string> DiscoverLocalVersionCatalog(
        string modulePath,
        IReadOnlyCollection<string> files)
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            modulePath,
            "gradle",
            "libs.versions.toml"));

        return files.Any(file => PathsEqual(file, catalogPath))
            ? [catalogPath]
            : [];
    }

    private static VersionCatalog ReadVersionCatalog(
        IReadOnlyCollection<string> catalogFiles,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var result = new VersionCatalog();
        foreach (var file in catalogFiles)
        {
            if (!File.Exists(file))
            {
                diagnostics.Add(Diagnostic(
                    "GradleVersionCatalogMissing",
                    "A discovered Gradle version catalog is no longer available.",
                    file));
                continue;
            }

            var section = string.Empty;
            var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var libraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var plugins = new Dictionary<string, VersionCatalog.Plugin>(StringComparer.OrdinalIgnoreCase);
            var pendingLibraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var skippingMultilineEntry = false;

            try
            {
                foreach (var rawLine in File.ReadLines(file))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#'))
                        continue;

                    if (skippingMultilineEntry)
                    {
                        if (line.Contains('}'))
                            skippingMultilineEntry = false;
                        continue;
                    }

                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        section = line[1..^1];
                        continue;
                    }

                    var assignment = CatalogAssignment.Match(line);
                    if (!assignment.Success)
                    {
                        if (section is "versions" or "libraries" or "plugins")
                        {
                            diagnostics.Add(Diagnostic(
                                "GradleVersionCatalogEntryUnsupported",
                                $"A version-catalog entry in [{section}] is not a supported single-line assignment: '{line}'.",
                                file));
                        }

                        continue;
                    }

                    var alias = NormalizeAlias(assignment.Groups["alias"].Value);
                    var value = assignment.Groups["value"].Value.Trim();

                    if (value.StartsWith('{') && !value.Contains('}'))
                    {
                        diagnostics.Add(Diagnostic(
                            "GradleVersionCatalogEntryUnsupported",
                            $"Version-catalog alias '{alias}' uses a multiline TOML table, which is outside the supported static subset.",
                            file));
                        skippingMultilineEntry = true;
                        continue;
                    }

                    if (section.Equals("versions", StringComparison.OrdinalIgnoreCase))
                    {
                        var literal = QuotedNotation.Match(value);
                        if (literal.Success)
                        {
                            versions[alias] = literal.Groups["notation"].Value;
                        }
                        else
                        {
                            diagnostics.Add(Diagnostic(
                                "GradleVersionCatalogVersionNotStatic",
                                $"Version alias '{alias}' does not use a supported literal string value.",
                                file));
                        }
                    }
                    else if (section.Equals("libraries", StringComparison.OrdinalIgnoreCase))
                    {
                        var module = CatalogModule.Match(value);
                        var shorthand = QuotedNotation.Match(value);
                        if (module.Success)
                        {
                            libraries[alias] = module.Groups["module"].Value;
                            pendingLibraries[alias] = value;
                        }
                        else if (shorthand.Success)
                        {
                            libraries[alias] = shorthand.Groups["notation"].Value;
                            pendingLibraries[alias] = value;
                        }
                        else
                        {
                            diagnostics.Add(Diagnostic(
                                "GradleVersionCatalogLibraryNotStatic",
                                $"Library alias '{alias}' does not use a supported module or shorthand notation.",
                                file));
                        }
                    }
                    else if (section.Equals("plugins", StringComparison.OrdinalIgnoreCase))
                    {
                        var id = Regex.Match(value, @"id\s*=\s*['""](?<id>[^'""]+)['""]");
                        if (!id.Success)
                        {
                            diagnostics.Add(Diagnostic(
                                "GradleVersionCatalogPluginNotStatic",
                                $"Plugin alias '{alias}' does not declare a supported literal plugin ID.",
                                file));
                            continue;
                        }

                        var version = CatalogVersion.Match(value);
                        var versionReference = CatalogVersionReference.Match(value);
                        var resolvedVersion = version.Success
                            ? version.Groups["version"].Value
                            : versionReference.Success
                                ? versions.GetValueOrDefault(NormalizeAlias(versionReference.Groups["reference"].Value))
                                : null;

                        if (versionReference.Success && resolvedVersion is null)
                        {
                            diagnostics.Add(Diagnostic(
                                "GradleVersionCatalogVersionReferenceNotResolved",
                                $"Plugin alias '{alias}' references missing version alias '{versionReference.Groups["reference"].Value}'.",
                                file));
                        }

                        plugins[alias] = new VersionCatalog.Plugin(
                            id.Groups["id"].Value,
                            resolvedVersion);
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic(
                    "GradleVersionCatalogReadFailed",
                    $"Gradle version catalog could not be read: {exception.Message}",
                    file));
                continue;
            }

            foreach (var entry in pendingLibraries)
            {
                var coordinates = libraries[entry.Key].Split(':');
                if (coordinates.Length is not (2 or 3)
                    || coordinates.Any(string.IsNullOrWhiteSpace))
                {
                    diagnostics.Add(Diagnostic(
                        "GradleVersionCatalogLibraryNotStatic",
                        $"Library alias '{entry.Key}' does not contain valid group:artifact coordinates.",
                        file));
                    continue;
                }

                var version = CatalogVersion.Match(entry.Value);
                var versionReference = CatalogVersionReference.Match(entry.Value);
                var resolvedVersion = coordinates.Length == 3
                    ? coordinates[2]
                    : version.Success
                        ? version.Groups["version"].Value
                        : versionReference.Success
                            ? versions.GetValueOrDefault(NormalizeAlias(versionReference.Groups["reference"].Value))
                            : null;

                if (versionReference.Success && resolvedVersion is null)
                {
                    diagnostics.Add(Diagnostic(
                        "GradleVersionCatalogVersionReferenceNotResolved",
                        $"Library alias '{entry.Key}' references missing version alias '{versionReference.Groups["reference"].Value}'.",
                        file));
                }

                result.Libraries[entry.Key] = new VersionCatalog.Library(
                    coordinates[0], coordinates[1], resolvedVersion);
            }

            foreach (var entry in plugins)
                result.Plugins[entry.Key] = entry.Value;
        }

        return result;
    }

    private static List<ProjectSourceRoot> DiscoverSourceRoots(
        string modulePath,
        IReadOnlyCollection<string> files,
        IReadOnlyCollection<GradleBuildModel.SourceSet> sourceSets,
        string buildFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var roots = new Dictionary<string, ProjectSourceRoot>(PathComparer);

        foreach (var sourceSet in sourceSets)
        {
            var ownership = sourceSet.IsTest
                ? ProjectSourceRootOwnership.Test
                : ProjectSourceRootOwnership.Production;
            foreach (var directory in sourceSet.JavaSourceDirectories)
            {
                var fullDirectory = Path.GetFullPath(directory);
                roots[fullDirectory] = new ProjectSourceRoot
                {
                    Directory = fullDirectory,
                    Ownership = ownership,
                    Origin = ProjectSourceRootOrigin.Conventional,
                    IsGenerated = false,
                    ContainsJavaSources = files.Any(file =>
                        Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase)
                        && IsSameOrDescendantPath(file, fullDirectory)),
                    EvidenceFile = buildFile
                };
            }
        }

        foreach (var sourceFile in files.Where(file =>
                     Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase)))
        {
            var relative = Path.GetRelativePath(modulePath, sourceFile)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var buildIndex = Array.FindIndex(relative, segment =>
                segment.Equals("build", StringComparison.OrdinalIgnoreCase));
            if (buildIndex < 0 || buildIndex + 2 >= relative.Length
                || !relative[buildIndex + 1].Equals("generated", StringComparison.OrdinalIgnoreCase)
                || !relative[buildIndex + 2].Equals("sources", StringComparison.OrdinalIgnoreCase))
                continue;

            var sourceSetIndex = Array.FindLastIndex(relative, segment =>
                segment.Equals("main", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("test", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("integrationTest", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("testFixtures", StringComparison.OrdinalIgnoreCase));

            ProjectSourceRootOwnership ownership;
            int rootLength;
            if (sourceSetIndex > buildIndex + 2)
            {
                ownership = relative[sourceSetIndex].Equals("main", StringComparison.OrdinalIgnoreCase)
                    ? ProjectSourceRootOwnership.Production
                    : ProjectSourceRootOwnership.Test;
                rootLength = sourceSetIndex + 1;
            }
            else
            {
                // A generated directory without an identifiable Gradle source set
                // remains visible but is not assigned to production or test.
                ownership = ProjectSourceRootOwnership.Unknown;
                rootLength = Math.Min(relative.Length, buildIndex + 4);
                diagnostics.Add(Diagnostic(
                    "GradleGeneratedSourceOwnershipUnknown",
                    $"Generated Java source '{sourceFile}' is under a generated-sources directory without an identifiable source-set owner.",
                    sourceFile));
            }

            var directory = Path.GetFullPath(Path.Combine(
                modulePath,
                Path.Combine(relative.Take(rootLength).ToArray())));
            roots[directory] = new ProjectSourceRoot
            {
                Directory = directory,
                Ownership = ownership,
                Origin = ProjectSourceRootOrigin.GeneratedConvention,
                IsGenerated = true,
                ContainsJavaSources = true,
                EvidenceFile = buildFile
            };
        }

        return roots.Values
            .OrderBy(root => root.Directory, PathComparer)
            .ToList();
    }

    private static List<GradleBuildModel.SourceSet> DiscoverConventionalSourceSets(
        string modulePath,
        IReadOnlyCollection<string> files)
    {
        var result = new List<GradleBuildModel.SourceSet>();
        foreach (var sourceSet in new[] { "main", "test", "integrationTest", "testFixtures" })
        {
            var directory = Path.Combine(modulePath, "src", sourceSet, "java");
            var exists = files.Any(file =>
                IsSameOrDescendantPath(file, directory));
            if (exists)
                result.Add(new GradleBuildModel.SourceSet(sourceSet, [directory], sourceSet != "main"));
        }

        return result;
    }

    private static string? ExtractBlock(string source, string name)
    {
        var match = Regex.Match(source, $@"\b{Regex.Escape(name)}\s*\{{");
        if (!match.Success)
            return null;

        var opening = source.IndexOf('{', match.Index);
        var depth = 0;
        var inString = false;
        var escaped = false;
        var quote = '\0';
        for (var index = opening; index < source.Length; index++)
        {
            var current = source[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == quote) inString = false;
                continue;
            }

            if (current is '\'' or '"')
            {
                inString = true;
                quote = current;
            }
            else if (current == '{') depth++;
            else if (current == '}' && --depth == 0)
                return source[(opening + 1)..index];
        }

        return null;
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
                else if (current is not '\r')
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
                else if (current is not '\n' and not '\r')
                {
                    characters[index] = ' ';
                }

                continue;
            }

            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == quote)
                    inString = false;
                continue;
            }

            if (current is '\'' or '"')
            {
                inString = true;
                quote = current;
            }
            else if (current == '/' && next == '/')
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

    private static bool IsBuildFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrDescendantPath(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string right) =>
        PathComparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private static string NormalizeAlias(string alias) =>
        alias.Replace('-', '.')
            .Replace('_', '.')
            .Trim('.');

    private static GradleBuildModel Incomplete(string buildFile, string code, string message) =>
        new()
        {
            BuildFile = buildFile,
            Dialect = Path.GetFileName(buildFile).EndsWith(".kts", StringComparison.OrdinalIgnoreCase)
                ? "Kotlin" : "Groovy",
            Diagnostics = [Diagnostic(code, message, buildFile)],
            IsComplete = false
        };

    private static AnalysisDiagnostic Diagnostic(string code, string message, string path) =>
        new()
        {
            Code = code,
            Component = nameof(GradleBuildModelResolver),
            Message = message,
            Target = path,
            AffectsCoverage = true
        };

    private sealed class VersionCatalog
    {
        public Dictionary<string, Library> Libraries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Plugin> Plugins { get; } = new(StringComparer.OrdinalIgnoreCase);
        public sealed record Library(string Group, string Artifact, string? Version);
        public sealed record Plugin(string Id, string? Version);
    }
}
