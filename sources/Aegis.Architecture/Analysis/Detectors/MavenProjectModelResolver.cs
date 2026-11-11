using System.Text.RegularExpressions;
using System.Xml.Linq;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Resolves the statically knowable portion of Maven POM models. Parent POMs
/// are followed only through local relative paths; no remote repository access
/// or Maven execution occurs.
/// </summary>
internal static class MavenProjectModelResolver
{
    private static readonly Regex PropertyReference = new(@"\$\{(?<name>[^}]+)\}", RegexOptions.Compiled);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static void Analyze(
        ProjectArchitectureContext context,
        IReadOnlyCollection<string> files)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(files);

        context.DependencyEvidence.RemoveAll(evidence =>
            evidence.Ecosystem.Equals("Maven", StringComparison.OrdinalIgnoreCase));

        var cache = new Dictionary<string, MavenProjectModel>(PathComparer);
        var visiting = new HashSet<string>(PathComparer);
        var pomFiles = files
            .Where(LanguageAndBuildDetector.IsMavenManifest)
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .ToArray();
        var knownPomFiles = pomFiles.ToHashSet(PathComparer);
        var coordinateIndex = BuildProjectCoordinateIndex(pomFiles);

        foreach (var pomFile in pomFiles)
        {
            var model = Resolve(pomFile, files, knownPomFiles, coordinateIndex, cache, visiting);
            var modulePath = Path.GetDirectoryName(pomFile);
            var module = context.Modules.FirstOrDefault(candidate =>
                modulePath is not null && PathsEqual(candidate.Path, modulePath));

            if (module is null)
                continue;

            module.MavenModel = model;
            module.SourceRoots = model.SourceRoots;
            if (!string.IsNullOrWhiteSpace(model.ProjectCoordinates.ArtifactId))
                module.Name = model.ProjectCoordinates.ArtifactId;
            module.ProjectKind = model.Packaging.Equals("war", StringComparison.OrdinalIgnoreCase)
                ? "WebApplication"
                : model.Packaging.Equals("pom", StringComparison.OrdinalIgnoreCase)
                    ? "BuildAggregator"
                    : "JavaProject";

            AddDependencyEvidence(context, module, model);
        }
    }

    private static MavenProjectModel Resolve(
        string pomFile,
        IReadOnlyCollection<string> files,
        IReadOnlySet<string> knownPomFiles,
        IReadOnlyDictionary<string, List<string>> coordinateIndex,
        IDictionary<string, MavenProjectModel> cache,
        ISet<string> visiting)
    {
        var fullPath = Path.GetFullPath(pomFile);
        if (cache.TryGetValue(fullPath, out var cached))
            return cached;

        if (!visiting.Add(fullPath))
        {
            return IncompleteModel(
                fullPath,
                "MavenParentCycle",
                "A cycle was detected while resolving local Maven parent POMs.");
        }

        try
        {
            XDocument document;
            try
            {
                document = XDocument.Load(fullPath, LoadOptions.None);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or System.Xml.XmlException)
            {
                var failed = IncompleteModel(
                    fullPath,
                    "MavenPomReadFailed",
                    $"The Maven POM could not be read: {exception.Message}");
                cache[fullPath] = failed;
                return failed;
            }

            var root = document.Root;
            if (root is null || !NameIs(root, "project"))
            {
                var failed = IncompleteModel(
                    fullPath,
                    "MavenPomInvalidRoot",
                    "The Maven POM does not contain a project root element.");
                cache[fullPath] = failed;
                return failed;
            }

            var parentElement = Child(root, "parent");
            var parentCoordinates = parentElement is null
                ? null
                : new MavenProjectModel.Coordinates(
                    Value(parentElement, "groupId"),
                    Value(parentElement, "artifactId"),
                    Value(parentElement, "version"));

            MavenProjectModel? parentModel = null;
            string? parentProjectFile = null;
            var parentResolutionStatus = parentElement is null ? "NotDeclared" : "ExternalDeclared";
            var diagnostics = new List<AnalysisDiagnostic>();
            var localProperties = new Dictionary<string, string>(StringComparer.Ordinal);
            var declaredProperties = Child(root, "properties");
            if (declaredProperties is not null)
            {
                foreach (var property in declaredProperties.Elements())
                    localProperties[property.Name.LocalName] = property.Value.Trim();
            }

            if (parentElement is not null)
            {
                var relativePathElement = Child(parentElement, "relativePath");
                var relativePath = relativePathElement is null
                    ? Path.Combine("..", "pom.xml")
                    : relativePathElement.Value.Trim();

                if (relativePath.Length > 0)
                {
                    string parentPath;
                    try
                    {
                        parentPath = Path.GetFullPath(Path.Combine(
                            Path.GetDirectoryName(fullPath)!,
                            relativePath));
                        if (Directory.Exists(parentPath))
                            parentPath = Path.Combine(parentPath, "pom.xml");
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                        parentPath = string.Empty;
                        diagnostics.Add(Diagnostic(
                            "MavenParentRelativePathInvalid",
                            $"Parent relativePath '{relativePath}' is not a valid local path: {exception.Message}",
                            fullPath));
                    }

                    if (parentPath.Length > 0 && File.Exists(parentPath) && knownPomFiles.Contains(parentPath))
                    {
                        var candidateParent = Resolve(
                            parentPath, files, knownPomFiles, coordinateIndex, cache, visiting);
                        var declaredGroup = ResolveValue(parentCoordinates?.GroupId, localProperties);
                        var declaredArtifact = ResolveValue(parentCoordinates?.ArtifactId, localProperties);
                        var declaredVersion = ResolveValue(parentCoordinates?.Version, localProperties);
                        var coordinatesMatch =
                            string.Equals(declaredGroup, candidateParent.ProjectCoordinates.GroupId, StringComparison.Ordinal)
                            && string.Equals(declaredArtifact, candidateParent.ProjectCoordinates.ArtifactId, StringComparison.Ordinal)
                            && string.Equals(declaredVersion, candidateParent.ProjectCoordinates.Version, StringComparison.Ordinal);

                        if (coordinatesMatch)
                        {
                            parentProjectFile = parentPath;
                            parentModel = candidateParent;
                            parentResolutionStatus = candidateParent.IsComplete
                                ? "ResolvedLocal"
                                : "ResolvedLocalIncomplete";
                        }
                        else
                        {
                            parentResolutionStatus = "LocalCoordinatesMismatch";
                            diagnostics.Add(Diagnostic(
                                "MavenParentCoordinatesMismatch",
                                $"The POM at relativePath '{relativePath}' does not match the declared parent coordinates '{parentCoordinates?.GroupId}:{parentCoordinates?.ArtifactId}:{parentCoordinates?.Version}'. It is not inherited; remote resolution is not attempted.",
                                fullPath));
                        }
                    }
                    else
                    {
                        diagnostics.Add(Diagnostic(
                            "MavenParentNotResolved",
                            $"Parent POM '{relativePath}' was not found among the scanned repository manifests. The declared external parent is retained, but files outside the scanned project are not read.",
                            fullPath));
                    }
                }
                else
                {
                    diagnostics.Add(Diagnostic(
                        "MavenParentNotResolved",
                        "The parent relativePath is explicitly disabled. The declared external parent is retained; remote resolution is not attempted.",
                        fullPath));
                }
            }

            if (parentModel is not null)
                diagnostics.AddRange(parentModel.Diagnostics);

            var properties = new Dictionary<string, string>(
                parentModel?.EffectiveProperties ?? new Dictionary<string, string>(),
                StringComparer.Ordinal);
            foreach (var property in localProperties)
                properties[property.Key] = property.Value;

            var groupId = ResolveValue(Value(root, "groupId") ?? parentModel?.ProjectCoordinates.GroupId, properties);
            var artifactId = ResolveValue(Value(root, "artifactId"), properties);
            var version = ResolveValue(Value(root, "version") ?? parentModel?.ProjectCoordinates.Version, properties);
            var parentGroup = ResolveValue(parentCoordinates?.GroupId, properties);
            var parentArtifact = ResolveValue(parentCoordinates?.ArtifactId, properties);
            var parentVersion = ResolveValue(parentCoordinates?.Version, properties);
            var coordinates = new MavenProjectModel.Coordinates(groupId, artifactId, version);
            var packaging = ResolveValue(Value(root, "packaging") ?? "jar", properties) ?? "jar";

            AddBuiltInProperties(properties, coordinates, parentModel?.ProjectCoordinates, packaging, fullPath);
            ResolveProperties(properties);
            foreach (var unresolvedProperty in properties.Where(property => PropertyReference.IsMatch(property.Value)))
            {
                diagnostics.Add(Diagnostic(
                    "MavenPropertyNotResolved",
                    $"Property '{unresolvedProperty.Key}' contains an unresolved expression: {unresolvedProperty.Value}.",
                    fullPath));
            }
            groupId = ResolveValue(groupId, properties);
            artifactId = ResolveValue(artifactId, properties);
            version = ResolveValue(version, properties);
            coordinates = new MavenProjectModel.Coordinates(groupId, artifactId, version);

            var declaredModules = Child(root, "modules")?
                .Elements()
                .Where(element => NameIs(element, "module"))
                .Select(element => ResolveValue(element.Value.Trim(), properties) ?? element.Value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? [];
            var reactorModuleFiles = ResolveReactorModuleFiles(
                declaredModules,
                fullPath,
                knownPomFiles,
                diagnostics);

            var bomImports = parentModel?.BomImports
                .Select(bomImport => bomImport with { IsInherited = true })
                .ToList() ?? [];
            var managed = parentModel?.ManagedDependencies
                .Select(dependency => dependency with { IsInherited = true })
                .ToList() ?? [];
            var localManaged = ParseDependencies(
                Child(Child(root, "dependencyManagement"), "dependencies"),
                properties,
                managed: true,
                inherited: false,
                sourceProjectFile: fullPath);

            foreach (var bom in localManaged.Where(dependency =>
                         dependency.Type.Equals("pom", StringComparison.OrdinalIgnoreCase)
                         && dependency.Scope.Equals("import", StringComparison.OrdinalIgnoreCase)))
            {
                var importedBom = FindLocalBomModel(
                    bom,
                    fullPath,
                    files,
                    knownPomFiles,
                    coordinateIndex,
                    cache,
                    visiting,
                    out var ambiguous);

                if (importedBom is null)
                {
                    bomImports.Add(new MavenProjectModel.BomImport(
                        new MavenProjectModel.Coordinates(bom.GroupId, bom.ArtifactId, bom.Version),
                        bom.SourceProjectFile,
                        ambiguous ? "AmbiguousLocalCandidates" : "ExternalOrUnavailable",
                        bom.IsInherited));
                    diagnostics.Add(Diagnostic(
                        ambiguous ? "MavenBomCoordinatesAmbiguous" : "MavenBomNotResolved",
                        ambiguous
                            ? $"More than one scanned POM declares imported BOM coordinates '{bom.GroupId}:{bom.ArtifactId}:{bom.Version}'. No candidate is selected."
                            : $"Imported BOM '{bom.GroupId}:{bom.ArtifactId}:{bom.Version}' is not available as a matching local POM. Remote repositories are not queried.",
                        fullPath));
                    continue;
                }

                bomImports.Add(new MavenProjectModel.BomImport(
                    new MavenProjectModel.Coordinates(bom.GroupId, bom.ArtifactId, bom.Version),
                    bom.SourceProjectFile,
                    importedBom.IsComplete ? "ResolvedLocal" : "LocalModelIncomplete",
                    bom.IsInherited));

                if (!importedBom.IsComplete)
                {
                    diagnostics.Add(Diagnostic(
                        "MavenBomModelIncomplete",
                        $"Imported BOM '{bom.GroupId}:{bom.ArtifactId}:{bom.Version}' has incomplete model evidence; only statically available managed entries are projected.",
                        importedBom.ProjectFile));
                }

                MergeDependencies(
                    managed,
                    importedBom.ManagedDependencies.Where(dependency =>
                        !(dependency.Type.Equals("pom", StringComparison.OrdinalIgnoreCase)
                          && dependency.Scope.Equals("import", StringComparison.OrdinalIgnoreCase))));
            }

            // Project-local dependency management takes precedence over imported
            // BOM entries, which in turn extend the inherited parent model.
            MergeDependencies(managed, localManaged);

            var effectiveDependencies = parentModel?.Dependencies
                .Select(dependency => dependency with { IsInherited = true })
                .ToList() ?? [];
            var localDependencies = ParseDependencies(
                Child(root, "dependencies"),
                properties,
                managed: false,
                inherited: false,
                sourceProjectFile: fullPath);

            for (var index = 0; index < localDependencies.Count; index++)
            {
                var dependency = localDependencies[index];
                var managedDependency = managed.LastOrDefault(candidate =>
                    DependencyKey(candidate) == DependencyKey(dependency));
                var versionIsManaged = string.IsNullOrWhiteSpace(dependency.Version)
                    && !string.IsNullOrWhiteSpace(managedDependency?.Version);
                var effectiveDependency = dependency with
                {
                    Version = versionIsManaged ? managedDependency!.Version : dependency.Version,
                    Scope = !string.IsNullOrWhiteSpace(dependency.Scope)
                        ? dependency.Scope
                        : !string.IsNullOrWhiteSpace(managedDependency?.Scope)
                            ? managedDependency.Scope
                            : "compile",
                    IsVersionManaged = versionIsManaged
                };
                localDependencies[index] = effectiveDependency;
            }

            MergeDependencies(effectiveDependencies, localDependencies);

            foreach (var unresolvedDependency in effectiveDependencies.Where(dependency =>
                         string.IsNullOrWhiteSpace(dependency.GroupId)
                         || string.IsNullOrWhiteSpace(dependency.ArtifactId)
                         || string.IsNullOrWhiteSpace(dependency.Version)
                         || PropertyReference.IsMatch(dependency.GroupId)
                         || PropertyReference.IsMatch(dependency.ArtifactId)
                         || PropertyReference.IsMatch(dependency.Version)))
            {
                diagnostics.Add(Diagnostic(
                    "MavenDependencyCoordinateIncomplete",
                    $"Dependency coordinates or effective version are incomplete: {unresolvedDependency.GroupId}:{unresolvedDependency.ArtifactId}:{unresolvedDependency.Version}.",
                    fullPath));
            }

            var plugins = parentModel?.Plugins
                .Select(plugin => plugin with { IsInherited = true })
                .ToList() ?? [];
            plugins.AddRange(ParsePlugins(root, properties, inherited: false, managed: false));

            var managedPlugins = parentModel?.ManagedPlugins
                .Select(plugin => plugin with { IsInherited = true })
                .ToList() ?? [];
            managedPlugins.AddRange(ParsePlugins(root, properties, inherited: false, managed: true));

            var processorPaths = parentModel?.AnnotationProcessorPaths
                .Select(dependency => dependency with { IsInherited = true })
                .ToList() ?? [];
            processorPaths.AddRange(ParseAnnotationProcessorPaths(root, properties, fullPath));

            var profiles = ParseProfiles(root, fullPath, properties);
            var profileIds = profiles.Select(profile => profile.Id).ToList();
            if (profiles.Count > 0)
            {
                diagnostics.Add(Diagnostic(
                    "MavenProfilesNotEvaluated",
                    "Profile activation is environment- and invocation-dependent. Profile properties, dependencies and plugins are retained as conditional evidence and are not merged into the unconditional model.",
                    fullPath));
            }

            var sourceRoots = DiscoverSourceRoots(root, fullPath, files, properties, diagnostics);

            var model = new MavenProjectModel
            {
                ProjectFile = fullPath,
                ProjectCoordinates = coordinates,
                ParentCoordinates = parentCoordinates is null
                    ? null
                    : new MavenProjectModel.Coordinates(parentGroup, parentArtifact, parentVersion),
                ParentProjectFile = parentProjectFile,
                ParentResolutionStatus = parentResolutionStatus,
                BomImports = bomImports,
                Profiles = profiles,
                Packaging = packaging,
                EffectiveProperties = properties,
                DeclaredModules = declaredModules,
                ReactorModuleFiles = reactorModuleFiles,
                Dependencies = effectiveDependencies,
                ManagedDependencies = managed,
                Plugins = plugins,
                ManagedPlugins = managedPlugins,
                AnnotationProcessorPaths = processorPaths,
                SourceRoots = sourceRoots,
                ProfileIds = profileIds,
                Diagnostics = diagnostics,
                IsComplete = diagnostics.Count == 0
            };

            cache[fullPath] = model;
            return model;
        }
        finally
        {
            visiting.Remove(fullPath);
        }
    }

    private static List<MavenProjectModel.Profile> ParseProfiles(
        XElement project,
        string projectFile,
        IReadOnlyDictionary<string, string> projectProperties)
    {
        return Child(project, "profiles")?
            .Elements()
            .Where(element => NameIs(element, "profile"))
            .Select(profile =>
            {
                var profileProperties = new Dictionary<string, string>(
                    projectProperties, StringComparer.Ordinal);
                var propertiesElement = Child(profile, "properties");
                if (propertiesElement is not null)
                {
                    foreach (var property in propertiesElement.Elements())
                        profileProperties[property.Name.LocalName] = property.Value.Trim();
                }

                ResolveProperties(profileProperties);
                var activation = Child(profile, "activation");
                var os = Child(activation, "os");
                var activationProperty = Child(activation, "property");
                var file = Child(activation, "file");
                return new MavenProjectModel.Profile(
                    Value(profile, "id") ?? string.Empty,
                    new MavenProjectModel.ProfileActivation(
                        bool.TryParse(Value(activation, "activeByDefault"), out var activeByDefault)
                            && activeByDefault,
                        Value(activation, "jdk"),
                        Value(os, "name"),
                        Value(os, "family"),
                        Value(os, "arch"),
                        Value(os, "version"),
                        Value(activationProperty, "name"),
                        Value(activationProperty, "value"),
                        Value(file, "exists"),
                        Value(file, "missing")),
                    profileProperties,
                    ParseDependencies(
                        Child(profile, "dependencies"), profileProperties,
                        managed: false, inherited: false, sourceProjectFile: projectFile),
                    ParseDependencies(
                        Child(Child(profile, "dependencyManagement"), "dependencies"),
                        profileProperties, managed: true, inherited: false,
                        sourceProjectFile: projectFile),
                    ParsePlugins(profile, profileProperties, inherited: false, managed: false),
                    ParsePlugins(profile, profileProperties, inherited: false, managed: true));
            })
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id))
            .ToList() ?? [];
    }

    private static List<string> ResolveReactorModuleFiles(
        IReadOnlyCollection<string> declaredModules,
        string projectFile,
        IReadOnlySet<string> knownPomFiles,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        var resolved = new HashSet<string>(PathComparer);

        foreach (var declaredModule in declaredModules)
        {
            if (PropertyReference.IsMatch(declaredModule))
            {
                diagnostics.Add(Diagnostic(
                    "MavenReactorModulePathNotResolved",
                    $"Reactor module path '{declaredModule}' contains an unresolved property expression.",
                    projectFile));
                continue;
            }

            string modulePom;
            try
            {
                var modulePath = Path.GetFullPath(Path.Combine(projectDirectory, declaredModule));
                modulePom = Path.GetFileName(modulePath).Equals("pom.xml", StringComparison.OrdinalIgnoreCase)
                    ? modulePath
                    : Path.Combine(modulePath, "pom.xml");
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                diagnostics.Add(Diagnostic(
                    "MavenReactorModulePathInvalid",
                    $"Reactor module path '{declaredModule}' is not a valid local path: {exception.Message}",
                    projectFile));
                continue;
            }

            if (!knownPomFiles.Contains(modulePom))
            {
                diagnostics.Add(Diagnostic(
                    "MavenReactorModuleNotFound",
                    $"Declared reactor module '{declaredModule}' does not map to a scanned POM. The module is not inferred.",
                    projectFile));
                continue;
            }

            resolved.Add(modulePom);
        }

        return resolved.OrderBy(path => path, PathComparer).ToList();
    }

    private static MavenProjectModel? FindLocalBomModel(
        MavenProjectModel.Dependency bom,
        string importingProjectFile,
        IReadOnlyCollection<string> files,
        IReadOnlySet<string> knownPomFiles,
        IReadOnlyDictionary<string, List<string>> coordinateIndex,
        IDictionary<string, MavenProjectModel> cache,
        ISet<string> visiting,
        out bool ambiguous)
    {
        ambiguous = false;
        if (string.IsNullOrWhiteSpace(bom.GroupId)
            || string.IsNullOrWhiteSpace(bom.ArtifactId)
            || string.IsNullOrWhiteSpace(bom.Version)
            || PropertyReference.IsMatch(bom.GroupId)
            || PropertyReference.IsMatch(bom.ArtifactId)
            || PropertyReference.IsMatch(bom.Version)
            || !coordinateIndex.TryGetValue(
                CoordinateKey(bom.GroupId, bom.ArtifactId, bom.Version),
                out var indexedCandidates))
        {
            return null;
        }

        var candidates = indexedCandidates
            .Where(candidate => !PathsEqual(candidate, importingProjectFile))
            .Distinct(PathComparer)
            .ToArray();
        if (candidates.Length == 0)
            return null;

        if (candidates.Length > 1)
        {
            ambiguous = true;
            return null;
        }

        var candidate = candidates[0];
        return knownPomFiles.Contains(candidate)
            ? Resolve(candidate, files, knownPomFiles, coordinateIndex, cache, visiting)
            : null;
    }

    private static Dictionary<string, List<string>> BuildProjectCoordinateIndex(
        IReadOnlyCollection<string> pomFiles)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pomFile in pomFiles)
        {
            try
            {
                var root = XDocument.Load(pomFile).Root;
                if (root is null || !NameIs(root, "project"))
                    continue;

                var properties = new Dictionary<string, string>(StringComparer.Ordinal);
                var declaredProperties = Child(root, "properties");
                if (declaredProperties is not null)
                {
                    foreach (var property in declaredProperties.Elements())
                        properties[property.Name.LocalName] = property.Value.Trim();
                }

                ResolveProperties(properties);

                var parent = Child(root, "parent");
                var group = ResolveValue(Value(root, "groupId") ?? Value(parent, "groupId"), properties);
                var artifact = ResolveValue(Value(root, "artifactId"), properties);
                var version = ResolveValue(Value(root, "version") ?? Value(parent, "version"), properties);
                if (string.IsNullOrWhiteSpace(group)
                    || string.IsNullOrWhiteSpace(artifact)
                    || string.IsNullOrWhiteSpace(version)
                    || PropertyReference.IsMatch(group)
                    || PropertyReference.IsMatch(artifact)
                    || PropertyReference.IsMatch(version))
                {
                    continue;
                }

                var key = CoordinateKey(group, artifact, version);
                if (!index.TryGetValue(key, out var paths))
                {
                    paths = [];
                    index.Add(key, paths);
                }

                paths.Add(Path.GetFullPath(pomFile));
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or System.Xml.XmlException)
            {
                // The primary resolver reports manifest read failures with
                // project-scoped diagnostics; the index never masks them.
            }
        }

        return index;
    }

    private static string CoordinateKey(string group, string artifact, string version) =>
        $"{group}\u001f{artifact}\u001f{version}";

    private static List<MavenProjectModel.Dependency> ParseDependencies(
        XElement? dependenciesElement,
        IReadOnlyDictionary<string, string> properties,
        bool managed,
        bool inherited,
        string sourceProjectFile)
    {
        if (dependenciesElement is null)
            return [];

        return dependenciesElement.Elements()
            .Where(element => NameIs(element, "dependency"))
            .Select(element =>
            {
                var exclusions = Child(element, "exclusions")?
                    .Elements()
                    .Where(exclusion => NameIs(exclusion, "exclusion"))
                    .Select(exclusion => new MavenProjectModel.Exclusion(
                        ResolveValue(Value(exclusion, "groupId"), properties),
                        ResolveValue(Value(exclusion, "artifactId"), properties)))
                    .ToList() ?? [];

                return new MavenProjectModel.Dependency(
                    ResolveValue(Value(element, "groupId"), properties),
                    ResolveValue(Value(element, "artifactId"), properties),
                    ResolveValue(Value(element, "version"), properties),
                    ResolveValue(Value(element, "scope"), properties) ?? string.Empty,
                    ResolveValue(Value(element, "type"), properties) ?? "jar",
                    ResolveValue(Value(element, "classifier"), properties),
                    bool.TryParse(ResolveValue(Value(element, "optional"), properties), out var optional)
                        && optional,
                    inherited,
                    false,
                    exclusions,
                    sourceProjectFile,
                    managed);
            })
            .ToList();
    }

    private static List<ProjectSourceRoot> DiscoverSourceRoots(
        XElement project,
        string projectFile,
        IReadOnlyCollection<string> files,
        IReadOnlyDictionary<string, string> properties,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        var roots = new Dictionary<string, ProjectSourceRoot>(PathComparer);

        void AddRoot(
            string directory,
            ProjectSourceRootOwnership ownership,
            ProjectSourceRootOrigin origin,
            bool generated,
            string evidenceFile,
            bool includeWhenEmpty)
        {
            var fullDirectory = Path.GetFullPath(directory);
            var containsJava = files.Any(file =>
                Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase)
                && IsSameOrDescendantPath(file, fullDirectory));
            if (!containsJava && !includeWhenEmpty)
                return;

            roots[fullDirectory] = new ProjectSourceRoot
            {
                Directory = fullDirectory,
                Ownership = ownership,
                Origin = origin,
                IsGenerated = generated,
                ContainsJavaSources = containsJava,
                EvidenceFile = evidenceFile
            };
        }

        AddRoot(Path.Combine(projectDirectory, "src", "main", "java"),
            ProjectSourceRootOwnership.Production, ProjectSourceRootOrigin.Conventional,
            false, projectFile, false);
        AddRoot(Path.Combine(projectDirectory, "src", "test", "java"),
            ProjectSourceRootOwnership.Test, ProjectSourceRootOrigin.Conventional,
            false, projectFile, false);

        var build = Child(project, "build");
        foreach (var (elementName, ownership) in new[]
                 {
                     ("sourceDirectory", ProjectSourceRootOwnership.Production),
                     ("testSourceDirectory", ProjectSourceRootOwnership.Test)
                 })
        {
            var declared = ResolveValue(Value(build, elementName), properties);
            if (string.IsNullOrWhiteSpace(declared))
                continue;

            if (PropertyReference.IsMatch(declared))
            {
                diagnostics.Add(Diagnostic(
                    "MavenSourceRootNotStaticallyResolved",
                    $"The configured {elementName} contains an unresolved property expression.",
                    projectFile));
                continue;
            }

            AddRoot(Path.IsPathRooted(declared)
                    ? declared
                    : Path.Combine(projectDirectory, declared),
                ownership,
                ProjectSourceRootOrigin.Explicit,
                declared.Contains("generated", StringComparison.OrdinalIgnoreCase),
                projectFile,
                true);
        }

        foreach (var sourceFile in files.Where(file =>
                     Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase)))
        {
            var relative = Path.GetRelativePath(projectDirectory, sourceFile)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var targetIndex = Array.FindIndex(relative, segment =>
                segment.Equals("target", StringComparison.OrdinalIgnoreCase));
            if (targetIndex != 0 || targetIndex + 1 >= relative.Length)
                continue;

            var generatedDirectory = relative[targetIndex + 1];
            var ownership = generatedDirectory.Equals("generated-test-sources", StringComparison.OrdinalIgnoreCase)
                ? ProjectSourceRootOwnership.Test
                : generatedDirectory.Equals("generated-sources", StringComparison.OrdinalIgnoreCase)
                    ? ProjectSourceRootOwnership.Production
                    : ProjectSourceRootOwnership.Unknown;
            if (ownership == ProjectSourceRootOwnership.Unknown)
                continue;

            var rootLength = Math.Min(relative.Length, targetIndex + 3);
            var root = Path.Combine(projectDirectory,
                Path.Combine(relative.Take(rootLength).ToArray()));
            AddRoot(root, ownership, ProjectSourceRootOrigin.GeneratedConvention,
                true, projectFile, false);
        }

        return roots.Values
            .OrderBy(root => root.Directory, PathComparer)
            .ToList();
    }

    private static bool IsSameOrDescendantPath(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static List<MavenProjectModel.Plugin> ParsePlugins(
        XElement root,
        IReadOnlyDictionary<string, string> properties,
        bool inherited,
        bool managed)
    {
        var build = Child(root, "build");
        var pluginsElement = managed
            ? Child(Child(build, "pluginManagement"), "plugins")
            : Child(build, "plugins");

        return (pluginsElement?.Elements() ?? Enumerable.Empty<XElement>())
            .Where(element => NameIs(element, "plugin"))
            .Select(plugin => new MavenProjectModel.Plugin(
                ResolveValue(Value(plugin, "groupId") ?? "org.apache.maven.plugins", properties),
                ResolveValue(Value(plugin, "artifactId"), properties),
                ResolveValue(Value(plugin, "version"), properties),
                inherited))
            .ToList();
    }

    private static List<MavenProjectModel.Dependency> ParseAnnotationProcessorPaths(
        XElement root,
        IReadOnlyDictionary<string, string> properties,
        string sourceProjectFile)
    {
        return root.Descendants()
            .Where(element => NameIs(element, "annotationProcessorPaths"))
            .SelectMany(element => element.Elements().Where(path => NameIs(path, "path")))
            .Select(path => new MavenProjectModel.Dependency(
                ResolveValue(Value(path, "groupId"), properties),
                ResolveValue(Value(path, "artifactId"), properties),
                ResolveValue(Value(path, "version"), properties),
                "annotationProcessor",
                "jar",
                null,
                false,
                false,
                false,
                [],
                sourceProjectFile,
                false))
            .ToList();
    }

    private static void MergeDependencies(
        List<MavenProjectModel.Dependency> target,
        IEnumerable<MavenProjectModel.Dependency> incoming)
    {
        foreach (var dependency in incoming)
        {
            var key = DependencyKey(dependency);
            var existingIndex = target.FindIndex(existing => DependencyKey(existing) == key);
            if (existingIndex >= 0)
                target[existingIndex] = dependency;
            else
                target.Add(dependency);
        }
    }

    private static string DependencyKey(MavenProjectModel.Dependency dependency) =>
        $"{dependency.GroupId}|{dependency.ArtifactId}|{dependency.Type}|{dependency.Classifier}";

    private static void AddDependencyEvidence(
        ProjectArchitectureContext context,
        ProjectModuleContext module,
        MavenProjectModel model)
    {
        foreach (var dependency in model.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.GroupId)
                || string.IsNullOrWhiteSpace(dependency.ArtifactId))
                continue;

            var isImport = dependency.Type.Equals("pom", StringComparison.OrdinalIgnoreCase)
                && dependency.Scope.Equals("import", StringComparison.OrdinalIgnoreCase);

            context.DependencyEvidence.Add(new ArchitectureDependencyEvidence
            {
                Id = $"maven:{dependency.GroupId}:{dependency.ArtifactId}:{dependency.Version}:{dependency.Scope}:{module.Path}",
                Name = $"{dependency.GroupId}:{dependency.ArtifactId}",
                Ecosystem = "Maven",
                Kind = isImport ? DependencyEvidenceKind.Platform : DependencyEvidenceKind.Package,
                DeclaredVersion = dependency.Version,
                ResolvedVersion = null,
                Scope = dependency.Scope,
                SourceProjectPath = module.Path,
                IsDirect = !dependency.IsInherited,
                IsTestOnly = dependency.Scope.Equals("test", StringComparison.OrdinalIgnoreCase),
                ResolutionStatus = string.IsNullOrWhiteSpace(dependency.Version)
                    || PropertyReference.IsMatch(dependency.Version)
                    ? DependencyResolutionStatus.PartiallyResolved
                    : DependencyResolutionStatus.Declared,
                Provenance =
                [
                    new SemanticEvidenceProvenance
                    {
                        AnalyzerId = "aegis.maven-model",
                        Source = SemanticEvidenceSource.BuildModel,
                        FilePath = dependency.SourceProjectFile,
                        Confidence = 1.0
                    }
                ]
            });
        }

        foreach (var bom in model.ManagedDependencies.Where(dependency =>
                     dependency.Type.Equals("pom", StringComparison.OrdinalIgnoreCase)
                     && dependency.Scope.Equals("import", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(bom.GroupId) || string.IsNullOrWhiteSpace(bom.ArtifactId))
                continue;

            context.DependencyEvidence.Add(new ArchitectureDependencyEvidence
            {
                Id = $"maven-bom:{bom.GroupId}:{bom.ArtifactId}:{bom.Version}:{module.Path}",
                Name = $"{bom.GroupId}:{bom.ArtifactId}",
                Ecosystem = "Maven",
                Kind = DependencyEvidenceKind.Platform,
                DeclaredVersion = bom.Version,
                Scope = "import",
                SourceProjectPath = module.Path,
                IsDirect = !bom.IsInherited,
                IsTestOnly = false,
                ResolutionStatus = DependencyResolutionStatus.Declared,
                Provenance =
                [
                    new SemanticEvidenceProvenance
                    {
                        AnalyzerId = "aegis.maven-model",
                        Source = SemanticEvidenceSource.BuildModel,
                        FilePath = bom.SourceProjectFile,
                        Confidence = 1.0
                    }
                ]
            });
        }
    }

    private static void AddBuiltInProperties(
        IDictionary<string, string> properties,
        MavenProjectModel.Coordinates coordinates,
        MavenProjectModel.Coordinates? parent,
        string packaging,
        string projectFile)
    {
        properties["project.groupId"] = coordinates.GroupId ?? string.Empty;
        properties["pom.groupId"] = coordinates.GroupId ?? string.Empty;
        properties["project.artifactId"] = coordinates.ArtifactId ?? string.Empty;
        properties["pom.artifactId"] = coordinates.ArtifactId ?? string.Empty;
        properties["project.version"] = coordinates.Version ?? string.Empty;
        properties["pom.version"] = coordinates.Version ?? string.Empty;
        properties["project.packaging"] = packaging;
        properties["project.parent.groupId"] = parent?.GroupId ?? string.Empty;
        properties["project.parent.artifactId"] = parent?.ArtifactId ?? string.Empty;
        properties["project.parent.version"] = parent?.Version ?? string.Empty;
        properties["basedir"] = Path.GetDirectoryName(projectFile) ?? string.Empty;
    }

    private static void ResolveProperties(Dictionary<string, string> properties)
    {
        for (var pass = 0; pass < 10; pass++)
        {
            var changed = false;
            foreach (var key in properties.Keys.ToArray())
            {
                var current = properties[key];
                var resolved = ResolveValue(current, properties);
                if (!string.Equals(current, resolved, StringComparison.Ordinal))
                {
                    properties[key] = resolved ?? string.Empty;
                    changed = true;
                }
            }

            if (!changed)
                return;
        }
    }

    private static string? ResolveValue(
        string? value,
        IReadOnlyDictionary<string, string> properties)
    {
        if (value is null)
            return null;

        var current = value.Trim();
        for (var pass = 0; pass < 10; pass++)
        {
            var replaced = PropertyReference.Replace(current, match =>
                properties.TryGetValue(match.Groups["name"].Value, out var replacement)
                    ? replacement
                    : match.Value);

            if (string.Equals(current, replaced, StringComparison.Ordinal))
                break;

            current = replaced;
        }

        return current;
    }

    private static MavenProjectModel IncompleteModel(
        string projectFile,
        string code,
        string message) =>
        new()
        {
            ProjectFile = projectFile,
            Diagnostics = [Diagnostic(code, message, projectFile)],
            IsComplete = false
        };

    private static AnalysisDiagnostic Diagnostic(string code, string message, string path) =>
        new()
        {
            Code = code,
            Component = nameof(MavenProjectModelResolver),
            Message = message,
            Target = path,
            AffectsCoverage = true
        };

    private static XElement? Child(XElement? element, string name) =>
        element?.Elements().FirstOrDefault(child => NameIs(child, name));

    private static string? Value(XElement? element, string name) =>
        Child(element, name)?.Value.Trim();

    private static bool NameIs(XElement element, string name) =>
        element.Name.LocalName.Equals(name, StringComparison.Ordinal);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
