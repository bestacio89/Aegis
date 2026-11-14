using System.Text.RegularExpressions;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Builds a bounded Android source-set and variant projection from literal build
/// declarations and observed conventional paths. Gradle is never evaluated.
/// </summary>
internal static class GradleAndroidSourceSetResolver
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static void Populate(
        string modulePath,
        string analysisRoot,
        IReadOnlyCollection<string> files,
        string? androidBlock,
        string buildFile,
        ICollection<AnalysisDiagnostic> diagnostics,
        GradleAndroidBuildModel model)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(model);

        var variants = ResolveVariants(model, buildFile, diagnostics);
        var knownSourceSetNames = CreateExpectedSourceSetNames(model, variants);
        var sourceSetNames = new HashSet<string>(knownSourceSetNames, StringComparer.Ordinal);
        var explicitDirectories = ParseExplicitSourceSets(
            androidBlock, modulePath, analysisRoot, buildFile, diagnostics);

        var observed = DiscoverObservedSourceSets(modulePath, files);
        foreach (var name in observed.Keys)
            sourceSetNames.Add(name);
        foreach (var name in explicitDirectories.Keys)
            sourceSetNames.Add(name);

        var sourceSets = new List<GradleAndroidSourceSet>();
        var sourceRoots = new Dictionary<string, ProjectSourceRoot>(PathComparer);
        foreach (var name in sourceSetNames.OrderBy(value => value, StringComparer.Ordinal))
        {
            var ownership = ResolveOwnership(name, knownSourceSetNames);
            observed.TryGetValue(name, out var discovered);
            explicitDirectories.TryGetValue(name, out var explicitSet);

            var javaDirectories = MergeDirectories(
                discovered?.JavaSourceDirectories,
                explicitSet?.JavaSourceDirectories);
            var kotlinDirectories = MergeDirectories(
                discovered?.KotlinSourceDirectories,
                explicitSet?.KotlinSourceDirectories);
            var resourceDirectories = MergeDirectories(
                discovered?.ResourceDirectories,
                explicitSet?.ResourceDirectories);
            var assetDirectories = MergeDirectories(
                discovered?.AssetDirectories,
                explicitSet?.AssetDirectories);
            var manifestFiles = MergeFiles(
                discovered?.ManifestFiles,
                explicitSet?.ManifestFiles);

            sourceSets.Add(new GradleAndroidSourceSet
            {
                Name = name,
                Ownership = ownership,
                JavaSourceDirectories = javaDirectories,
                KotlinSourceDirectories = kotlinDirectories,
                ManifestFiles = manifestFiles,
                ResourceDirectories = resourceDirectories,
                AssetDirectories = assetDirectories,
                EvidenceFile = buildFile
            });

            AddSourceRoots(sourceRoots, javaDirectories, "Java", ownership, buildFile);
            AddSourceRoots(sourceRoots, kotlinDirectories, "Kotlin", ownership, buildFile);
        }

        model.Variants = variants;
        model.SourceSets = sourceSets;
        model.SourceRoots = sourceRoots.Values
            .OrderBy(root => root.Directory, PathComparer)
            .ToList();
        model.IsComplete = diagnostics.All(diagnostic => !diagnostic.AffectsCoverage);
    }

    private static List<GradleAndroidVariant> ResolveVariants(
        GradleAndroidBuildModel model,
        string buildFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var buildTypes = model.BuildTypes
            .Select(buildType => buildType.Name)
            .Concat(["debug", "release"])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var flavors = model.ProductFlavors;
        if (flavors.Count == 0)
        {
            return buildTypes.Select(buildType =>
                new GradleAndroidVariant(buildType, [], buildType)).ToList();
        }

        var dimensions = model.FlavorDimensions.Count > 0
            ? model.FlavorDimensions
            : flavors.Where(flavor => !string.IsNullOrWhiteSpace(flavor.Dimension))
                .Select(flavor => flavor.Dimension!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        if (flavors.Any(flavor => string.IsNullOrWhiteSpace(flavor.Dimension)))
        {
            diagnostics.Add(Diagnostic(
                "GradleAndroidFlavorDimensionNotStaticallyResolved",
                "One or more product flavors have no statically resolved dimension; the complete variant matrix cannot be derived.",
                buildFile));
        }

        if (dimensions.Count == 0 || dimensions.Any(dimension =>
                !flavors.Any(flavor => string.Equals(flavor.Dimension, dimension, StringComparison.Ordinal))))
        {
            diagnostics.Add(Diagnostic(
                "GradleAndroidVariantMatrixIncomplete",
                "The declared product flavors do not provide at least one statically resolved flavor for every flavor dimension.",
                buildFile));
            return [];
        }

        var choices = dimensions.Select(dimension => flavors
            .Where(flavor => string.Equals(flavor.Dimension, dimension, StringComparison.Ordinal))
            .Select(flavor => flavor.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()).ToArray();

        var combinations = new List<List<string>> { [] };
        foreach (var dimensionChoices in choices)
        {
            combinations = combinations
                .SelectMany(existing => dimensionChoices.Select(choice =>
                    existing.Append(choice).ToList()))
                .ToList();
        }

        var result = new List<GradleAndroidVariant>();
        foreach (var combination in combinations)
        foreach (var buildType in buildTypes)
        {
            var name = string.Concat(combination.Select(Capitalize)) + Capitalize(buildType);
            result.Add(new GradleAndroidVariant(name, combination, buildType));
        }

        return result.OrderBy(variant => variant.Name, StringComparer.Ordinal).ToList();
    }

    private static HashSet<string> CreateExpectedSourceSetNames(
        GradleAndroidBuildModel model,
        IReadOnlyCollection<GradleAndroidVariant> variants)
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "main", "test", "androidTest", "debug", "release"
        };

        foreach (var flavor in model.ProductFlavors)
        {
            names.Add(flavor.Name);
            names.Add("test" + Capitalize(flavor.Name));
            names.Add("androidTest" + Capitalize(flavor.Name));
        }

        foreach (var buildType in model.BuildTypes)
            names.Add(buildType.Name);

        foreach (var variant in variants)
        {
            names.Add(variant.Name);
            names.Add("test" + Capitalize(variant.Name));
            names.Add("androidTest" + Capitalize(variant.Name));
        }

        names.Add("testFixtures");
        return names;
    }

    private static Dictionary<string, ObservedSourceSet> DiscoverObservedSourceSets(
        string modulePath,
        IReadOnlyCollection<string> files)
    {
        var result = new Dictionary<string, ObservedSourceSet>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var fullFile = Path.GetFullPath(file);
            if (!IsSameOrDescendantPath(fullFile, modulePath))
                continue;

            var relative = Path.GetRelativePath(modulePath, fullFile)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relative.Length < 3
                || !relative[0].Equals("src", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(relative[1]))
                continue;

            var sourceSetName = relative[1];
            var sourceSet = GetOrAdd(result, sourceSetName);
            var category = relative[2];

            if (category.Equals("AndroidManifest.xml", StringComparison.OrdinalIgnoreCase)
                && relative.Length == 3)
            {
                sourceSet.ManifestFiles.Add(fullFile);
                continue;
            }

            if (category.Equals("manifest", StringComparison.OrdinalIgnoreCase)
                && relative.Length >= 4
                && relative[3].Equals("AndroidManifest.xml", StringComparison.OrdinalIgnoreCase))
            {
                sourceSet.ManifestFiles.Add(fullFile);
                continue;
            }

            if (category.Equals("java", StringComparison.OrdinalIgnoreCase))
            {
                AddIfSourceFile(sourceSet.JavaSourceDirectories, fullFile, modulePath, relative, ".java");
                AddIfSourceFile(sourceSet.KotlinSourceDirectories, fullFile, modulePath, relative, ".kt");
            }
            else if (category.Equals("kotlin", StringComparison.OrdinalIgnoreCase))
                AddIfSourceFile(sourceSet.KotlinSourceDirectories, fullFile, modulePath, relative, ".kt");
            else if (category.Equals("res", StringComparison.OrdinalIgnoreCase))
                AddDirectory(sourceSet.ResourceDirectories, Path.Combine(modulePath, "src", sourceSetName, "res"));
            else if (category.Equals("assets", StringComparison.OrdinalIgnoreCase))
                AddDirectory(sourceSet.AssetDirectories, Path.Combine(modulePath, "src", sourceSetName, "assets"));
        }

        return result;
    }

    private static void AddIfSourceFile(
        ICollection<string> directories,
        string file,
        string modulePath,
        IReadOnlyList<string> relative,
        string extension)
    {
        if (!Path.GetExtension(file).Equals(extension, StringComparison.OrdinalIgnoreCase))
            return;

        AddDirectory(directories, Path.GetFullPath(Path.Combine(
            modulePath, "src", relative[1], relative[2])));
    }

    private static Dictionary<string, ExplicitSourceSet> ParseExplicitSourceSets(
        string? androidBlock,
        string modulePath,
        string analysisRoot,
        string buildFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, ExplicitSourceSet>(StringComparer.Ordinal);
        var sourceSetsBlock = ExtractBlock(androidBlock, "sourceSets");
        if (sourceSetsBlock is null)
            return result;

        var declarations = Regex.Matches(
            sourceSetsBlock,
            @"(?:getByName|maybeCreate|create)\s*\(\s*['""](?<name>[^'""]+)['""]\s*\)\s*\{|(?<name>[A-Za-z][A-Za-z0-9_]*)\s*\{",
            RegexOptions.CultureInvariant);

        foreach (Match declaration in declarations)
        {
            var name = declaration.Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var opening = sourceSetsBlock.IndexOf('{', declaration.Index);
            var body = ExtractBlockAt(sourceSetsBlock, opening);
            if (body is null)
            {
                diagnostics.Add(Diagnostic(
                    "GradleAndroidSourceSetNotStaticallyResolved",
                    $"Android source set '{name}' contains a block that could not be isolated safely.",
                    buildFile));
                continue;
            }

            var sourceSet = GetOrAdd(result, name);
            ParseDirectoryDeclarations(body, "java", sourceSet.JavaSourceDirectories,
                modulePath, analysisRoot, name, buildFile, diagnostics);
            ParseDirectoryDeclarations(body, "kotlin", sourceSet.KotlinSourceDirectories,
                modulePath, analysisRoot, name, buildFile, diagnostics);
            ParseDirectoryDeclarations(body, "res", sourceSet.ResourceDirectories,
                modulePath, analysisRoot, name, buildFile, diagnostics);
            ParseDirectoryDeclarations(body, "assets", sourceSet.AssetDirectories,
                modulePath, analysisRoot, name, buildFile, diagnostics);
            ParseManifestDeclaration(body, sourceSet.ManifestFiles,
                modulePath, analysisRoot, name, buildFile, diagnostics);
        }

        return result;
    }

    private static void ParseDirectoryDeclarations(
        string body,
        string property,
        ICollection<string> destination,
        string modulePath,
        string analysisRoot,
        string sourceSetName,
        string buildFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var declarations = Regex.Matches(
            body,
            @"\b" + Regex.Escape(property)
            + @"\s*\.\s*(?:srcDirs|srcDir|setSrcDirs)\s*(?:\(\s*(?<args>[^\n)]*)\)|=\s*(?<assignment>[^\n]+))",
            RegexOptions.CultureInvariant);

        foreach (Match declaration in declarations)
        {
            var expression = declaration.Groups["args"].Success
                ? declaration.Groups["args"].Value
                : declaration.Groups["assignment"].Value;
            var values = Regex.Matches(expression, @"['""](?<path>[^'""]+)['""]")
                .Cast<Match>()
                .Select(match => match.Groups["path"].Value)
                .ToArray();
            var residue = Regex.Replace(expression, @"['""][^'""]+['""]|\s|,|\[|\]|\(|\)", string.Empty);

            if (values.Length == 0 || residue.Length > 0 || values.Any(value =>
                    value.Contains('$') || value.Contains('{')))
            {
                diagnostics.Add(Diagnostic(
                    "GradleAndroidSourceDirectoryNotStaticallyResolved",
                    $"Android {property} directories for source set '{sourceSetName}' use dynamic or unsupported expressions.",
                    buildFile));
                continue;
            }

            foreach (var value in values)
            {
                if (!TryResolvePath(value, modulePath, analysisRoot, out var directory))
                {
                    diagnostics.Add(Diagnostic(
                        "GradleAndroidSourceDirectoryOutsideAnalysisRoot",
                        $"Android {property} directory '{value}' for source set '{sourceSetName}' is invalid or outside the analysis root.",
                        buildFile));
                    continue;
                }

                AddDirectory(destination, directory);
            }
        }
    }

    private static void ParseManifestDeclaration(
        string body,
        ICollection<string> destination,
        string modulePath,
        string analysisRoot,
        string sourceSetName,
        string buildFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var declaration = Regex.Match(
            body,
            @"\bmanifest\s*\.\s*srcFile\s*(?:\(\s*(?<path>[^)]+)\)|(?<assignment>[^\n]+))",
            RegexOptions.CultureInvariant);
        if (!declaration.Success)
            return;

        var expression = declaration.Groups["path"].Success
            ? declaration.Groups["path"].Value
            : declaration.Groups["assignment"].Value;
        var value = Regex.Match(expression, @"['""](?<path>[^'""]+)['""]");
        if (!value.Success || value.Groups["path"].Value.Contains('$')
            || !TryResolvePath(value.Groups["path"].Value, modulePath, analysisRoot, out var manifest))
        {
            diagnostics.Add(Diagnostic(
                "GradleAndroidManifestNotStaticallyResolved",
                $"Android manifest for source set '{sourceSetName}' uses a dynamic, unsupported or out-of-root path.",
                buildFile));
            return;
        }

        if (!Path.GetFileName(manifest).Equals("AndroidManifest.xml", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Diagnostic(
                "GradleAndroidManifestPathInvalid",
                $"Android manifest for source set '{sourceSetName}' does not identify AndroidManifest.xml.",
                buildFile));
            return;
        }

        AddFile(destination, manifest);
    }

    private static void AddSourceRoots(
        IDictionary<string, ProjectSourceRoot> roots,
        IEnumerable<string> directories,
        string language,
        ProjectSourceRootOwnership ownership,
        string buildFile)
    {
        foreach (var directory in directories)
        {
            var fullDirectory = Path.GetFullPath(directory);
            if (roots.TryGetValue(fullDirectory, out var existing))
            {
                var languages = existing.Languages.Append(language)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var mergedOwnership = existing.Ownership == ownership
                    ? ownership
                    : ProjectSourceRootOwnership.Unknown;
                roots[fullDirectory] = existing with
                {
                    Ownership = mergedOwnership,
                    Languages = languages,
                    ContainsJavaSources = languages.Contains("Java", StringComparer.OrdinalIgnoreCase)
                };
                continue;
            }

            roots[fullDirectory] = new ProjectSourceRoot
            {
                Directory = fullDirectory,
                Ownership = ownership,
                Origin = ProjectSourceRootOrigin.Conventional,
                Languages = [language],
                ContainsJavaSources = language.Equals("Java", StringComparison.OrdinalIgnoreCase),
                EvidenceFile = buildFile
            };
        }
    }

    private static ProjectSourceRootOwnership ResolveOwnership(
        string name,
        IReadOnlySet<string> expectedSourceSets)
    {
        if (name.Equals("main", StringComparison.Ordinal)
            || name.Equals("debug", StringComparison.Ordinal)
            || name.Equals("release", StringComparison.Ordinal)
            || expectedSourceSets.Contains(name)
                && !IsTestSourceSet(name))
            return ProjectSourceRootOwnership.Production;

        if (IsTestSourceSet(name))
            return ProjectSourceRootOwnership.Test;

        return ProjectSourceRootOwnership.Unknown;
    }

    private static bool IsTestSourceSet(string name) =>
        name.Equals("test", StringComparison.OrdinalIgnoreCase)
        || name.Equals("androidTest", StringComparison.OrdinalIgnoreCase)
        || name.Equals("testFixtures", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("test", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("androidTest", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("AndroidTest", StringComparison.OrdinalIgnoreCase);

    private static bool TryResolvePath(
        string value,
        string modulePath,
        string analysisRoot,
        out string path)
    {
        path = string.Empty;
        try
        {
            path = Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(modulePath, value));
            return IsSameOrDescendantPath(path, analysisRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static List<string> MergeDirectories(
        IEnumerable<string>? first,
        IEnumerable<string>? second) =>
        (first ?? [])
            .Concat(second ?? [])
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .OrderBy(path => path, PathComparer)
            .ToList();

    private static List<string> MergeFiles(
        IEnumerable<string>? first,
        IEnumerable<string>? second) =>
        (first ?? [])
            .Concat(second ?? [])
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .OrderBy(path => path, PathComparer)
            .ToList();

    private static void AddDirectory(ICollection<string> values, string directory)
    {
        if (!values.Any(existing => PathComparer.Equals(existing, directory)))
            values.Add(Path.GetFullPath(directory));
    }

    private static void AddFile(ICollection<string> values, string file)
    {
        if (!values.Any(existing => PathComparer.Equals(existing, file)))
            values.Add(Path.GetFullPath(file));
    }

    private static ObservedSourceSet GetOrAdd(
        IDictionary<string, ObservedSourceSet> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value))
        {
            value = new ObservedSourceSet();
            values.Add(name, value);
        }

        return value;
    }

    private static ExplicitSourceSet GetOrAdd(
        IDictionary<string, ExplicitSourceSet> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value))
        {
            value = new ExplicitSourceSet();
            values.Add(name, value);
        }

        return value;
    }

    private static string? ExtractBlock(string? source, string name)
    {
        if (source is null)
            return null;

        var match = Regex.Match(source, @"\b" + Regex.Escape(name) + @"\s*\{",
            RegexOptions.CultureInvariant);
        return match.Success
            ? ExtractBlockAt(source, source.IndexOf('{', match.Index))
            : null;
    }

    private static string? ExtractBlockAt(string source, int opening)
    {
        if (opening < 0 || opening >= source.Length || source[opening] != '{')
            return null;

        var depth = 0;
        var quote = '\0';
        var inString = false;
        var escaped = false;
        for (var index = opening; index < source.Length; index++)
        {
            var current = source[index];
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
            else if (current == '{')
            {
                depth++;
            }
            else if (current == '}' && --depth == 0)
            {
                return source[(opening + 1)..index];
            }
        }

        return null;
    }

    private static bool IsSameOrDescendantPath(string path, string parent)
    {
        var fullPath = Path.GetFullPath(path);
        var fullParent = Path.GetFullPath(parent);
        if (PathComparer.Equals(fullPath, fullParent))
            return true;

        var prefix = fullParent.EndsWith(Path.DirectorySeparatorChar)
            ? fullParent
            : fullParent + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static AnalysisDiagnostic Diagnostic(string code, string message, string target) => new()
    {
        Code = code,
        Component = "GradleAndroidSourceSetResolver",
        Message = message,
        Target = target,
        AffectsCoverage = true
    };

    private sealed class ObservedSourceSet
    {
        public List<string> JavaSourceDirectories { get; } = [];
        public List<string> KotlinSourceDirectories { get; } = [];
        public List<string> ManifestFiles { get; } = [];
        public List<string> ResourceDirectories { get; } = [];
        public List<string> AssetDirectories { get; } = [];
    }

    private sealed class ExplicitSourceSet
    {
        public List<string> JavaSourceDirectories { get; } = [];
        public List<string> KotlinSourceDirectories { get; } = [];
        public List<string> ManifestFiles { get; } = [];
        public List<string> ResourceDirectories { get; } = [];
        public List<string> AssetDirectories { get; } = [];
    }
}
