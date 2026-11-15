using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Resolves direct Maven/Gradle declarations only from local artifact caches.
/// It does not access the network, execute build tools, or infer transitive
/// dependencies. Any declared external dependency keeps the result partial
/// until a complete resolved graph is supplied by a trusted provider.
/// </summary>
public sealed class LocalJvmClasspathResolver : IJvmClasspathResolver
{
    private readonly string _mavenRepository;
    private readonly string _gradleArtifactCache;
    private readonly AarArtifactExtractor _aarArtifactExtractor;

    public LocalJvmClasspathResolver(
        string? mavenRepository = null,
        string? gradleArtifactCache = null,
        string? aarExtractionCache = null)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _mavenRepository = Path.GetFullPath(
            mavenRepository
            ?? Environment.GetEnvironmentVariable("MAVEN_REPO_LOCAL")
            ?? Environment.GetEnvironmentVariable("M2_REPO")
            ?? Path.Combine(userProfile, ".m2", "repository"));
        _gradleArtifactCache = Path.GetFullPath(
            gradleArtifactCache
            ?? Environment.GetEnvironmentVariable("GRADLE_USER_HOME")
            ?? Path.Combine(userProfile, ".gradle", "caches", "modules-2", "files-2.1"));
        _aarArtifactExtractor = new AarArtifactExtractor(aarExtractionCache);
        if (Environment.GetEnvironmentVariable("GRADLE_USER_HOME") is { Length: > 0 } gradleHome
            && gradleArtifactCache is null)
        {
            _gradleArtifactCache = Path.Combine(
                Path.GetFullPath(gradleHome),
                "caches", "modules-2", "files-2.1");
        }
    }

    public JvmClasspathResolution Resolve(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectModuleContext> projectModules,
        bool includeTestCode) =>
        ResolveCore(module, projectModules, includeTestCode, null);

    public JvmClasspathResolution Resolve(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectModuleContext> projectModules,
        IReadOnlyCollection<GradleBuildModel.Dependency> selectedGradleDependencies)
    {
        ArgumentNullException.ThrowIfNull(selectedGradleDependencies);
        return ResolveCore(module, projectModules, includeTestCode: true, selectedGradleDependencies);
    }

    private JvmClasspathResolution ResolveCore(
        ProjectModuleContext module,
        IReadOnlyCollection<ProjectModuleContext> projectModules,
        bool includeTestCode,
        IReadOnlyCollection<GradleBuildModel.Dependency>? selectedGradleDependencies)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(projectModules);

        var entries = new HashSet<string>(PathComparer);
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        var hasDeclaredDependencies = false;
        var hasUnresolvedDependencyGraph = false;

        if (module.MavenModel is { } maven)
        {
            foreach (var dependency in maven.Dependencies)
            {
                if (dependency.Scope.Equals("test", StringComparison.OrdinalIgnoreCase)
                    && !includeTestCode)
                    continue;

                if (dependency.Scope.Equals("runtime", StringComparison.OrdinalIgnoreCase)
                    || dependency.Type.Equals("pom", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrWhiteSpace(dependency.GroupId)
                    || string.IsNullOrWhiteSpace(dependency.ArtifactId))
                    continue;

                var projectModule = projectModules.FirstOrDefault(candidate =>
                    !ReferenceEquals(candidate, module)
                    && candidate.MavenModel is { } projectModel
                    && string.Equals(projectModel.ProjectCoordinates.GroupId, dependency.GroupId, StringComparison.Ordinal)
                    && string.Equals(projectModel.ProjectCoordinates.ArtifactId, dependency.ArtifactId, StringComparison.Ordinal));
                if (projectModule is not null)
                {
                    hasDeclaredDependencies = true;
                    unresolved.Add($"project:{projectModule.Name}");
                    hasUnresolvedDependencyGraph = true;
                    continue;
                }

                hasDeclaredDependencies = true;
                hasUnresolvedDependencyGraph = true;
                var coordinate = $"{dependency.GroupId}:{dependency.ArtifactId}:{dependency.Version ?? "unspecified"}";
                if (!TryResolveMavenArtifact(dependency, out var artifactPaths))
                {
                    unresolved.Add(coordinate);
                    continue;
                }

                foreach (var artifactPath in artifactPaths)
                    entries.Add(artifactPath);
            }
        }

        if (module.GradleBuildModel is { } gradle)
        {
            foreach (var dependency in selectedGradleDependencies ?? gradle.Dependencies)
            {
                if (dependency.IsPlatform
                    || dependency.IsEnforcedPlatform
                    || dependency.IsAnnotationProcessor
                    || !IsCompileConfiguration(dependency.Configuration, includeTestCode))
                    continue;

                if (!string.IsNullOrWhiteSpace(dependency.ProjectPath))
                {
                    // Android project outputs are resolved separately with exact variant provenance.
                    hasDeclaredDependencies = true;
                    if (selectedGradleDependencies is null)
                    {
                        unresolved.Add($"project:{dependency.ProjectPath}");
                        hasUnresolvedDependencyGraph = true;
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(dependency.Group)
                    || string.IsNullOrWhiteSpace(dependency.Artifact))
                    continue;

                hasDeclaredDependencies = true;
                hasUnresolvedDependencyGraph = true;
                var coordinate = $"{dependency.Group}:{dependency.Artifact}:{dependency.Version ?? "unspecified"}";
                if (!TryResolveGradleArtifact(dependency, out var artifactPaths))
                {
                    unresolved.Add(coordinate);
                    continue;
                }

                foreach (var artifactPath in artifactPaths)
                    entries.Add(artifactPath);
            }
        }

        var orderedEntries = entries.OrderBy(path => path, PathComparer).ToArray();
        var orderedUnresolved = unresolved.OrderBy(value => value, StringComparer.Ordinal).ToArray();

        // The local cache contains direct declared artifacts only. Maven/Gradle
        // transitive graphs are intentionally not guessed from cache contents.
        return new JvmClasspathResolution(
            orderedEntries,
            orderedUnresolved,
            hasDeclaredDependencies,
            !hasUnresolvedDependencyGraph);
    }

    private bool TryResolveMavenArtifact(
        MavenProjectModel.Dependency dependency,
        out IReadOnlyList<string> artifactPaths)
    {
        artifactPaths = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(dependency.Version)
            || dependency.Version.Contains("$" + "{", StringComparison.Ordinal))
            return false;

        var groupPath = dependency.GroupId!.Replace('.', Path.DirectorySeparatorChar);
        var fileName = $"{dependency.ArtifactId}-{dependency.Version}";
        if (!string.IsNullOrWhiteSpace(dependency.Classifier))
            fileName += $"-{dependency.Classifier}";
        else if (dependency.Type.Equals("test-jar", StringComparison.OrdinalIgnoreCase))
            fileName += "-tests";

        var isAar = dependency.Type.Equals("aar", StringComparison.OrdinalIgnoreCase);
        var extension = isAar ? ".aar" : ".jar";
        var path = Path.Combine(
            _mavenRepository,
            groupPath,
            dependency.ArtifactId!,
            dependency.Version,
            fileName + extension);
        if (!File.Exists(path))
            return false;

        try
        {
            artifactPaths = isAar
                ? _aarArtifactExtractor.Extract(path)
                : [Path.GetFullPath(path)];
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
    }

    private bool TryResolveGradleArtifact(
        GradleBuildModel.Dependency dependency,
        out IReadOnlyList<string> artifactPaths)
    {
        artifactPaths = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(dependency.Version)
            || dependency.Version.Contains("$" + "{", StringComparison.Ordinal))
            return false;

        var versionDirectory = Path.Combine(
            _gradleArtifactCache,
            dependency.Group!,
            dependency.Artifact!,
            dependency.Version);
        if (!Directory.Exists(versionDirectory))
            return false;

        var prefix = $"{dependency.Artifact}-{dependency.Version}";
        var matches = Directory.EnumerateFiles(
                versionDirectory,
                "*.*",
                SearchOption.AllDirectories)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                var extension = Path.GetExtension(fileName);
                return (extension.Equals(".jar", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".aar", StringComparison.OrdinalIgnoreCase))
                    && fileName.StartsWith(prefix, StringComparison.Ordinal)
                    && fileName.Length > prefix.Length
                    && fileName[prefix.Length] is '.' or '-'
                    && !fileName.EndsWith("-sources.jar", StringComparison.OrdinalIgnoreCase)
                    && !fileName.EndsWith("-javadoc.jar", StringComparison.OrdinalIgnoreCase);
            })
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .OrderBy(path => path, PathComparer)
            .ToArray();

        if (matches.Length == 0)
            return false;

        try
        {
            var resolved = new List<string>();
            foreach (var path in matches)
            {
                if (Path.GetExtension(path).Equals(".aar", StringComparison.OrdinalIgnoreCase))
                    resolved.AddRange(_aarArtifactExtractor.Extract(path));
                else
                    resolved.Add(path);
            }

            artifactPaths = resolved.Distinct(PathComparer).OrderBy(path => path, PathComparer).ToArray();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            artifactPaths = Array.Empty<string>();
            return false;
        }
    }

    private static bool IsCompileConfiguration(string configuration, bool includeTestCode)
    {
        var value = configuration.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        if (value.Contains("runtimeonly", StringComparison.Ordinal)
            || value.Contains("annotationprocessor", StringComparison.Ordinal))
            return false;

        if (!includeTestCode && value.StartsWith("test", StringComparison.Ordinal))
            return false;

        return value.Contains("implementation", StringComparison.Ordinal)
            || value.Contains("api", StringComparison.Ordinal)
            || value.Contains("compile", StringComparison.Ordinal)
            || value.Contains("provided", StringComparison.Ordinal);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
