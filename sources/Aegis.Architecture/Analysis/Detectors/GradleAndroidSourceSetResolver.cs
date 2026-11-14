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
    private const int MaximumDerivedVariants = 512;

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

        model.TestFixturesEnabled = ResolveTestFixturesEnabled(androidBlock);
        var variants = ResolveVariants(model, buildFile, diagnostics);
        var knownSourceSetNames = CreateExpectedSourceSetNames(model, variants);
        var knownTestSourceSetNames = CreateExpectedTestSourceSetNames(model, variants);
        var sourceSetNames = new HashSet<string>(knownSourceSetNames, StringComparer.Ordinal);
        var explicitDirectories = ParseExplicitSourceSets(
            androidBlock, modulePath, analysisRoot, buildFile, diagnostics);

        var observed = DiscoverObservedSourceSets(modulePath, files);
        var generated = DiscoverGeneratedSourceSets(
            modulePath, files, knownSourceSetNames, knownTestSourceSetNames);
        foreach (var name in observed.Keys)
            sourceSetNames.Add(name);
        foreach (var name in generated.Keys)
            sourceSetNames.Add(name);
        foreach (var name in explicitDirectories.Keys)
            sourceSetNames.Add(name);

        var sourceSets = new List<GradleAndroidSourceSet>();
        var sourceRoots = new Dictionary<string, ProjectSourceRoot>(PathComparer);
        foreach (var name in sourceSetNames.OrderBy(value => value, StringComparer.Ordinal))
        {
            var ownership = ResolveOwnership(name, knownSourceSetNames, knownTestSourceSetNames);
            observed.TryGetValue(name, out var discovered);
            generated.TryGetValue(name, out var generatedSet);
            explicitDirectories.TryGetValue(name, out var explicitSet);
            var hasSourceEvidence = discovered is not null || generatedSet is not null || explicitSet is not null;
            if (ownership == ProjectSourceRootOwnership.Unknown && hasSourceEvidence)
            {
                diagnostics.Add(Diagnostic(
                    "GradleAndroidSourceSetOwnershipUnknown",
                    $"Android source set '{name}' is present but does not match a statically derived production or test source set.",
                    buildFile));
            }

            var javaDirectories = ResolveDirectories(
                discovered?.JavaSourceDirectories,
                explicitSet?.JavaSourceDirectories,
                explicitSet?.ReplacesJavaSourceDirectories == true);
            var kotlinDirectories = ResolveDirectories(
                discovered?.KotlinSourceDirectories,
                explicitSet?.KotlinSourceDirectories,
                explicitSet?.ReplacesKotlinSourceDirectories == true);
            var resourceDirectories = ResolveDirectories(
                discovered?.ResourceDirectories,
                explicitSet?.ResourceDirectories,
                explicitSet?.ReplacesResourceDirectories == true);
            var assetDirectories = ResolveDirectories(
                discovered?.AssetDirectories,
                explicitSet?.AssetDirectories,
                explicitSet?.ReplacesAssetDirectories == true);
            var manifestFiles = explicitSet?.ReplacesManifest == true
                ? MergeFiles(explicitSet.ManifestFiles, null)
                : MergeFiles(discovered?.ManifestFiles, explicitSet?.ManifestFiles);
            var generatedJavaDirectories = generatedSet?.JavaSourceDirectories ?? [];
            var generatedKotlinDirectories = generatedSet?.KotlinSourceDirectories ?? [];

            var applicableVariants = ResolveApplicableVariants(
                name, ownership, variants, knownTestSourceSetNames);
            if (name == "testFixtures" && model.TestFixturesEnabled != true)
            {
                applicableVariants = [];
                if (hasSourceEvidence)
                {
                    diagnostics.Add(Diagnostic(
                        "GradleAndroidTestFixturesNotEnabled",
                        "Android test-fixture sources were found, but android.testFixtures.enable is not statically true.",
                        buildFile));
                }
            }

            sourceSets.Add(new GradleAndroidSourceSet
            {
                Name = name,
                Ownership = ownership,
                TestKind = ResolveTestKind(name, ownership, knownTestSourceSetNames),
                AppliesToVariants = applicableVariants,
                JavaSourceDirectories = javaDirectories,
                KotlinSourceDirectories = kotlinDirectories,
                ExplicitJavaSourceDirectories = explicitSet?.JavaSourceDirectories ?? [],
                ExplicitKotlinSourceDirectories = explicitSet?.KotlinSourceDirectories ?? [],
                GeneratedJavaSourceDirectories = generatedJavaDirectories,
                GeneratedKotlinSourceDirectories = generatedKotlinDirectories,
                ManifestFiles = manifestFiles,
                ExplicitManifestFiles = explicitSet?.ManifestFiles ?? [],
                ResourceDirectories = resourceDirectories,
                ExplicitResourceDirectories = explicitSet?.ResourceDirectories ?? [],
                AssetDirectories = assetDirectories,
                ExplicitAssetDirectories = explicitSet?.AssetDirectories ?? [],
                EvidenceFile = buildFile
            });

            if (explicitSet?.ReplacesJavaSourceDirectories != true)
                AddSourceRoots(sourceRoots, discovered?.JavaSourceDirectories ?? [], "Java", ownership,
                    ProjectSourceRootOrigin.Conventional, buildFile);
            if (explicitSet?.ReplacesKotlinSourceDirectories != true)
                AddSourceRoots(sourceRoots, discovered?.KotlinSourceDirectories ?? [], "Kotlin", ownership,
                    ProjectSourceRootOrigin.Conventional, buildFile);
            AddSourceRoots(sourceRoots, explicitSet?.JavaSourceDirectories ?? [], "Java", ownership,
                ProjectSourceRootOrigin.Explicit, buildFile);
            AddSourceRoots(sourceRoots, explicitSet?.KotlinSourceDirectories ?? [], "Kotlin", ownership,
                ProjectSourceRootOrigin.Explicit, buildFile);
            AddSourceRoots(sourceRoots, generatedJavaDirectories, "Java", ownership,
                ProjectSourceRootOrigin.GeneratedConvention, buildFile, isGenerated: true);
            AddSourceRoots(sourceRoots, generatedKotlinDirectories, "Kotlin", ownership,
                ProjectSourceRootOrigin.GeneratedConvention, buildFile, isGenerated: true);
        }

        model.Variants = variants;
        model.SourceSets = sourceSets;
        model.SourceRoots = sourceRoots.Values
            .OrderBy(root => root.Directory, PathComparer)
            .ToList();
        model.IsComplete = diagnostics.All(diagnostic => !diagnostic.AffectsCoverage);
    }

    private static bool? ResolveTestFixturesEnabled(string? androidBlock)
    {
        var testFixturesBlock = ExtractBlock(androidBlock, "testFixtures");
        if (testFixturesBlock is null)
            return null;

        var match = Regex.Match(
            testFixturesBlock,
            @"\benable\s*=\s*(?<value>true|false)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && bool.TryParse(match.Groups["value"].Value, out var enabled)
            ? enabled
            : null;
    }

    private static List<GradleAndroidVariant> ResolveVariants(
        GradleAndroidBuildModel model,
        string buildFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        if (model.ModuleKind == "Unknown")
        {
            diagnostics.Add(Diagnostic(
                "GradleAndroidVariantModuleKindUnknown",
                "Android variant derivation is unavailable because the Android Gradle Plugin module kind was not statically resolved.",
                buildFile));
            return [];
        }

        var buildTypes = model.BuildTypes
            .Select(buildType => buildType.Name)
            .Concat(["debug", "release"])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (buildTypes.Length > MaximumDerivedVariants)
        {
            diagnostics.Add(Diagnostic(
                "GradleAndroidVariantMatrixLimitExceeded",
                $"The declared build types exceed Aegis' static derivation limit of {MaximumDerivedVariants} variants.",
                buildFile));
            return [];
        }

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

        long combinationCount = 1;
        foreach (var dimensionChoices in choices)
        {
            if (dimensionChoices.Length == 0
                || combinationCount > MaximumDerivedVariants / dimensionChoices.Length)
            {
                diagnostics.Add(Diagnostic(
                    "GradleAndroidVariantMatrixLimitExceeded",
                    $"The declared flavor dimensions exceed Aegis' static derivation limit of {MaximumDerivedVariants} variants.",
                    buildFile));
                return [];
            }

            combinationCount *= dimensionChoices.Length;
        }

        if (combinationCount * buildTypes.Length > MaximumDerivedVariants)
        {
            diagnostics.Add(Diagnostic(
                "GradleAndroidVariantMatrixLimitExceeded",
                $"The declared flavor/build-type matrix exceeds Aegis' static derivation limit of {MaximumDerivedVariants} variants.",
                buildFile));
            return [];
        }

        var combinations = new List<List<string>> { new() };
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
            var flavorPrefix = combination.Count == 0
                ? string.Empty
                : combination[0] + string.Concat(combination.Skip(1).Select(Capitalize));
            var name = flavorPrefix + Capitalize(buildType);
            result.Add(new GradleAndroidVariant(name, combination, buildType));
        }

        if (result.Select(variant => variant.Name).Distinct(StringComparer.Ordinal).Count() != result.Count)
        {
            diagnostics.Add(Diagnostic(
                "GradleAndroidVariantNameConflict",
                "Multiple flavor combinations produce the same Android variant name; variant-to-source-set mapping is ambiguous.",
                buildFile));
            return [];
        }

        return result.OrderBy(variant => variant.Name, StringComparer.Ordinal).ToList();
    }

    private static HashSet<string> CreateExpectedSourceSetNames(
        GradleAndroidBuildModel model,
        IReadOnlyCollection<GradleAndroidVariant> variants)
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "main", "test", "androidTest", "testFixtures"
        };

        if (model.ModuleKind == "Unknown")
            return names;

        names.Add("debug");
        names.Add("release");
        foreach (var flavor in model.ProductFlavors)
        {
            names.Add(flavor.Name);
            names.Add("test" + Capitalize(flavor.Name));
            names.Add("androidTest" + Capitalize(flavor.Name));
        }

        foreach (var buildType in model.BuildTypes)
        {
            names.Add(buildType.Name);
            names.Add("test" + Capitalize(buildType.Name));
            names.Add("androidTest" + Capitalize(buildType.Name));
        }

        foreach (var variant in variants)
        {
            names.Add(variant.Name);
            names.Add("test" + Capitalize(variant.Name));
            names.Add("androidTest" + Capitalize(variant.Name));
        }

        names.Add("testFixtures");
        return names;
    }

    private static HashSet<string> CreateExpectedTestSourceSetNames(
        GradleAndroidBuildModel model,
        IReadOnlyCollection<GradleAndroidVariant> variants)
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "test", "androidTest", "testFixtures"
        };

        if (model.ModuleKind == "Unknown")
            return names;

        foreach (var flavor in model.ProductFlavors)
        {
            names.Add("test" + Capitalize(flavor.Name));
            names.Add("androidTest" + Capitalize(flavor.Name));
        }

        foreach (var buildType in model.BuildTypes)
        {
            names.Add("test" + Capitalize(buildType.Name));
            names.Add("androidTest" + Capitalize(buildType.Name));
        }

        foreach (var variant in variants)
        {
            names.Add("test" + Capitalize(variant.Name));
            names.Add("androidTest" + Capitalize(variant.Name));
        }

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

    private static Dictionary<string, GeneratedSourceSet> DiscoverGeneratedSourceSets(
        string modulePath,
        IReadOnlyCollection<string> files,
        IReadOnlySet<string> knownSourceSetNames,
        IReadOnlySet<string> knownTestSourceSetNames)
    {
        var result = new Dictionary<string, GeneratedSourceSet>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var fullFile = Path.GetFullPath(file);
            if (!IsSameOrDescendantPath(fullFile, modulePath))
                continue;

            var relative = Path.GetRelativePath(modulePath, fullFile)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relative.Length < 5
                || !relative[0].Equals("build", StringComparison.OrdinalIgnoreCase)
                || !relative[1].Equals("generated", StringComparison.OrdinalIgnoreCase))
                continue;

            var extension = Path.GetExtension(fullFile);
            var language = extension.Equals(".java", StringComparison.OrdinalIgnoreCase)
                ? "Java"
                : extension.Equals(".kt", StringComparison.OrdinalIgnoreCase)
                    ? "Kotlin"
                    : null;
            if (language is null)
                continue;

            string rawSourceSetName;
            string generatedRoot;
            if (relative[2].Equals("ksp", StringComparison.OrdinalIgnoreCase)
                && relative.Length >= 6
                && relative[4].Equals(language.Equals("Java", StringComparison.Ordinal) ? "java" : "kotlin",
                    StringComparison.OrdinalIgnoreCase))
            {
                rawSourceSetName = relative[3];
                generatedRoot = Path.Combine(modulePath, "build", "generated", "ksp",
                    rawSourceSetName, relative[4]);
            }
            else if (relative[2].Equals("source", StringComparison.OrdinalIgnoreCase)
                     && relative.Length >= 6)
            {
                rawSourceSetName = relative[4];
                generatedRoot = Path.Combine(modulePath, "build", "generated", "source",
                    relative[3], rawSourceSetName);
            }
            else if (relative[2].Equals("ap_generated_sources", StringComparison.OrdinalIgnoreCase)
                     && relative.Length >= 6
                     && relative[4].Equals("out", StringComparison.OrdinalIgnoreCase))
            {
                rawSourceSetName = relative[3];
                generatedRoot = Path.Combine(modulePath, "build", "generated",
                    "ap_generated_sources", rawSourceSetName, "out");
            }
            else if (relative[2].Equals("sources", StringComparison.OrdinalIgnoreCase)
                     && relative.Length >= 7
                     && (relative[4].Equals("java", StringComparison.OrdinalIgnoreCase)
                         || relative[4].Equals("kotlin", StringComparison.OrdinalIgnoreCase)))
            {
                rawSourceSetName = relative[5];
                generatedRoot = Path.Combine(modulePath, "build", "generated", "sources",
                    relative[3], relative[4], rawSourceSetName);
            }
            else
            {
                continue;
            }

            var sourceSetName = ResolveGeneratedSourceSetName(
                rawSourceSetName, knownSourceSetNames, knownTestSourceSetNames);
            var sourceSet = GetOrAdd(result, sourceSetName);
            if (language.Equals("Java", StringComparison.Ordinal))
                AddDirectory(sourceSet.JavaSourceDirectories, generatedRoot);
            else
                AddDirectory(sourceSet.KotlinSourceDirectories, generatedRoot);
        }

        return result;
    }

    private static string ResolveGeneratedSourceSetName(
        string rawName,
        IReadOnlySet<string> knownSourceSetNames,
        IReadOnlySet<string> knownTestSourceSetNames)
    {
        if (knownSourceSetNames.Contains(rawName))
            return rawName;

        if (rawName.EndsWith("UnitTest", StringComparison.Ordinal))
        {
            var candidate = "test" + Capitalize(rawName[..^"UnitTest".Length]);
            if (knownTestSourceSetNames.Contains(candidate))
                return candidate;
        }

        if (rawName.EndsWith("AndroidTest", StringComparison.Ordinal))
        {
            var candidate = "androidTest" + Capitalize(rawName[..^"AndroidTest".Length]);
            if (knownTestSourceSetNames.Contains(candidate))
                return candidate;
        }

        return rawName;
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
        var factoryDeclarationCount = Regex.Matches(
            sourceSetsBlock,
            @"\b(?:getByName|maybeCreate|create)\s*\(",
            RegexOptions.CultureInvariant).Count;
        var resolvedFactoryDeclarationCount = declarations.Cast<Match>().Count(match =>
            match.Value.StartsWith("getByName", StringComparison.Ordinal)
            || match.Value.StartsWith("maybeCreate", StringComparison.Ordinal)
            || match.Value.StartsWith("create", StringComparison.Ordinal));
        if (factoryDeclarationCount > resolvedFactoryDeclarationCount)
        {
            diagnostics.Add(Diagnostic(
                "GradleAndroidSourceSetNotStaticallyResolved",
                "One or more Android source-set factory declarations use dynamic names and cannot be mapped safely.",
                buildFile));
        }

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
                modulePath, analysisRoot, name, buildFile, diagnostics,
                replaces => sourceSet.ReplacesJavaSourceDirectories = replaces);
            ParseDirectoryDeclarations(body, "kotlin", sourceSet.KotlinSourceDirectories,
                modulePath, analysisRoot, name, buildFile, diagnostics,
                replaces => sourceSet.ReplacesKotlinSourceDirectories = replaces);
            ParseDirectoryDeclarations(body, "res", sourceSet.ResourceDirectories,
                modulePath, analysisRoot, name, buildFile, diagnostics,
                replaces => sourceSet.ReplacesResourceDirectories = replaces);
            ParseDirectoryDeclarations(body, "assets", sourceSet.AssetDirectories,
                modulePath, analysisRoot, name, buildFile, diagnostics,
                replaces => sourceSet.ReplacesAssetDirectories = replaces);
            ParseManifestDeclaration(body, sourceSet.ManifestFiles,
                modulePath, analysisRoot, name, buildFile, diagnostics,
                () => sourceSet.ReplacesManifest = true);
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
        ICollection<AnalysisDiagnostic> diagnostics,
        Action<bool> setReplaces)
    {
        var declarationPattern = @"\b" + Regex.Escape(property)
            + @"\s*\.\s*(?<operation>srcDirs|srcDir|setSrcDirs)\s*(?:\(\s*(?<args>[^\n)]*)\)|\+=\s*(?<appendAssignment>[^\n]+)|=\s*(?<assignment>[^\n]+)|\s+(?<groovy>[^\n]+))";
        var declarations = Regex.Matches(body, declarationPattern, RegexOptions.CultureInvariant);
        var declarationCount = Regex.Matches(
            body,
            @"\b" + Regex.Escape(property) + @"\s*\.\s*(?:srcDirs|srcDir|setSrcDirs)\b",
            RegexOptions.CultureInvariant).Count;
        if (declarations.Count < declarationCount)
        {
            diagnostics.Add(Diagnostic(
                "GradleAndroidSourceDirectoryNotStaticallyResolved",
                $"One or more Android {property} source directory declarations for source set '{sourceSetName}' use unsupported syntax.",
                buildFile));
        }

        foreach (Match declaration in declarations)
        {
            var operation = declaration.Groups["operation"].Value;
            var replaces = declaration.Groups["assignment"].Success
                || operation.Equals("setSrcDirs", StringComparison.Ordinal);
            if (replaces)
            {
                destination.Clear();
                setReplaces(true);
            }

            var expression = declaration.Groups["args"].Success
                ? declaration.Groups["args"].Value
                : declaration.Groups["appendAssignment"].Success
                    ? declaration.Groups["appendAssignment"].Value
                    : declaration.Groups["assignment"].Success
                        ? declaration.Groups["assignment"].Value
                        : declaration.Groups["groovy"].Value;
            var values = Regex.Matches(expression, @"['""](?<path>[^'""]+)['""]")
                .Cast<Match>()
                .Select(match => match.Groups["path"].Value)
                .ToArray();
            var residue = Regex.Replace(
                expression,
                @"['""][^'""]+['""]|\b(?:file|files|listOf|setOf|arrayOf)\b|\s|,|\[|\]|\(|\)",
                string.Empty);

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
        ICollection<AnalysisDiagnostic> diagnostics,
        Action markOverride)
    {
        var declaration = Regex.Match(
            body,
            @"\bmanifest\s*\.\s*srcFile\s*(?:\(\s*(?<path>[^)]+)\)|(?<assignment>[^\n]+))",
            RegexOptions.CultureInvariant);
        if (!declaration.Success)
            return;

        destination.Clear();
        markOverride();
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
        ProjectSourceRootOrigin origin,
        string buildFile,
        bool isGenerated = false)
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
                    Origin = SelectOrigin(existing.Origin, origin),
                    IsGenerated = existing.IsGenerated || isGenerated,
                    Languages = languages,
                    ContainsJavaSources = languages.Contains("Java", StringComparer.OrdinalIgnoreCase)
                };
                continue;
            }

            roots[fullDirectory] = new ProjectSourceRoot
            {
                Directory = fullDirectory,
                Ownership = ownership,
                Origin = origin,
                IsGenerated = isGenerated,
                Languages = [language],
                ContainsJavaSources = language.Equals("Java", StringComparison.OrdinalIgnoreCase),
                EvidenceFile = buildFile
            };
        }
    }

    private static GradleAndroidTestKind ResolveTestKind(
        string name,
        ProjectSourceRootOwnership ownership,
        IReadOnlySet<string> expectedTestSourceSets)
    {
        if (ownership == ProjectSourceRootOwnership.Unknown)
            return GradleAndroidTestKind.Unknown;
        if (!expectedTestSourceSets.Contains(name))
            return GradleAndroidTestKind.None;
        if (name.Equals("testFixtures", StringComparison.Ordinal))
            return GradleAndroidTestKind.Fixtures;
        if (name.StartsWith("androidTest", StringComparison.Ordinal)
            || name.EndsWith("AndroidTest", StringComparison.Ordinal))
            return GradleAndroidTestKind.Instrumentation;

        return GradleAndroidTestKind.Unit;
    }

    private static List<string> ResolveApplicableVariants(
        string sourceSetName,
        ProjectSourceRootOwnership ownership,
        IReadOnlyCollection<GradleAndroidVariant> variants,
        IReadOnlySet<string> expectedTestSourceSets)
    {
        if (ownership == ProjectSourceRootOwnership.Unknown)
            return [];

        if (sourceSetName is "main" or "test" or "androidTest" or "testFixtures")
            return variants.Select(variant => variant.Name).ToList();

        var isTest = expectedTestSourceSets.Contains(sourceSetName);
        var testPrefix = sourceSetName.StartsWith("androidTest", StringComparison.Ordinal)
            ? "androidTest"
            : sourceSetName.StartsWith("test", StringComparison.Ordinal)
                ? "test"
                : string.Empty;
        var targetName = isTest && testPrefix.Length > 0
            ? sourceSetName[testPrefix.Length..]
            : sourceSetName;

        if (targetName.Length == 0)
            return variants.Select(variant => variant.Name).ToList();

        return variants
            .Where(variant =>
                variant.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)
                || variant.BuildType.Equals(targetName, StringComparison.OrdinalIgnoreCase)
                || variant.ProductFlavors.Any(flavor =>
                    flavor.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
            .Select(variant => variant.Name)
            .ToList();
    }

    private static ProjectSourceRootOrigin SelectOrigin(
        ProjectSourceRootOrigin existing,
        ProjectSourceRootOrigin incoming)
    {
        if (existing == ProjectSourceRootOrigin.Explicit || incoming == ProjectSourceRootOrigin.Explicit)
            return ProjectSourceRootOrigin.Explicit;
        if (existing == ProjectSourceRootOrigin.GeneratedConvention
            || incoming == ProjectSourceRootOrigin.GeneratedConvention)
            return ProjectSourceRootOrigin.GeneratedConvention;
        if (existing == ProjectSourceRootOrigin.Observed || incoming == ProjectSourceRootOrigin.Observed)
            return ProjectSourceRootOrigin.Observed;

        return ProjectSourceRootOrigin.Conventional;
    }

    private static ProjectSourceRootOwnership ResolveOwnership(
        string name,
        IReadOnlySet<string> expectedSourceSets,
        IReadOnlySet<string> expectedTestSourceSets)
    {
        if (expectedTestSourceSets.Contains(name))
            return ProjectSourceRootOwnership.Test;

        return expectedSourceSets.Contains(name)
            ? ProjectSourceRootOwnership.Production
            : ProjectSourceRootOwnership.Unknown;
    }

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

    private static List<string> ResolveDirectories(
        IEnumerable<string>? conventional,
        IEnumerable<string>? explicitDirectories,
        bool replaceConventional) =>
        replaceConventional
            ? MergeDirectories(null, explicitDirectories)
            : MergeDirectories(conventional, explicitDirectories);

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

    private static GeneratedSourceSet GetOrAdd(
        IDictionary<string, GeneratedSourceSet> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value))
        {
            value = new GeneratedSourceSet();
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

    private sealed class GeneratedSourceSet
    {
        public List<string> JavaSourceDirectories { get; } = [];
        public List<string> KotlinSourceDirectories { get; } = [];
    }

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
        public bool ReplacesJavaSourceDirectories { get; set; }
        public bool ReplacesKotlinSourceDirectories { get; set; }
        public bool ReplacesManifest { get; set; }
        public bool ReplacesResourceDirectories { get; set; }
        public bool ReplacesAssetDirectories { get; set; }
    }
}
