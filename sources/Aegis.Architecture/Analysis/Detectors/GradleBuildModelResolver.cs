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

    private static readonly Regex KotlinDslPluginDeclaration = new(
        @"^\s*kotlin\s*\(\s*['""](?<plugin>[^'""]+)['""]\s*\)\s*(?:version\s*['""](?<version>[^'""]+)['""])?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KotlinLanguageVersionOption = new(
        @"\blanguageVersion\s*(?:\.set\s*\(\s*(?:KotlinVersion\.fromVersion\s*\(\s*)?)?(?:=\s*)?['""](?<version>\d+\.\d+)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KotlinApiVersionOption = new(
        @"\bapiVersion\s*(?:\.set\s*\(\s*(?:KotlinVersion\.fromVersion\s*\(\s*)?)?(?:=\s*)?['""](?<version>\d+\.\d+)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KotlinJvmTargetLiteral = new(
        @"\bjvmTarget\s*(?:=\s*|\.set\s*\(\s*)['""](?<version>\d+(?:\.\d+)?)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KotlinJvmTargetEnum = new(
        @"\bjvmTarget\s*\.set\s*\(\s*(?:JvmTarget\.)?JVM_(?<version>\d+(?:_\d+)*)",
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

    private static readonly Regex GenericTestTaskRegistration = new(
        @"\btasks\s*\.\s*(?:register|named|create)\s*<\s*(?:org\.gradle\.api\.tasks\.testing\.)?Test\s*>\s*\(\s*['""](?<name>[^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitTestTaskRegistration = new(
        @"\btasks\s*\.\s*(?:register|named|create)\s*\(\s*['""](?<name>[^'""]+)['""]\s*,\s*(?:org\.gradle\.api\.tasks\.testing\.)?Test(?:::class)?\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GroovyTestTaskRegistration = new(
        @"\btask\s+(?<name>[A-Za-z][A-Za-z0-9_]*)\s*\(\s*type\s*:\s*(?:org\.gradle\.api\.tasks\.testing\.)?Test\s*\)",
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
            if (sourceSetBlock is not null)
            {
                var explicitSourceSets = ParseExplicitSourceSets(
                    sourceSetBlock, module.Path, context.RootPath, buildFile, diagnostics);
                sourceSets = MergeSourceSets(sourceSets, explicitSourceSets);
            }

            if (Regex.IsMatch(source, @"\btoolchain\b", RegexOptions.CultureInvariant)
                && !toolchainMatch.Success)
            {
                diagnostics.Add(Diagnostic(
                    "GradleToolchainNotStaticallyResolved",
                    "A Java toolchain is declared but its language version is not a supported literal JavaLanguageVersion.of(...) expression.",
                    buildFile));
            }

            var testTasks = ParseTestTasks(source, sourceSets);
            var sourceRoots = DiscoverSourceRoots(module.Path, files, sourceSets, buildFile, diagnostics);
            var androidModel = ParseAndroidBuildModel(
                source, plugins, catalog, files, module.Path, owningRoot?.Path, sourceRoots, diagnostics, buildFile);
            var kotlinJvmPlugins = plugins
                .Where(plugin => plugin.Id == "org.jetbrains.kotlin.jvm")
                .ToArray();
            var kotlinAndroidPlugins = plugins
                .Where(plugin => plugin.Id == "org.jetbrains.kotlin.android")
                .ToArray();
            var kotlinCompilerPlugins = kotlinJvmPlugins
                .Concat(kotlinAndroidPlugins)
                .ToArray();
            var kotlinPluginVersions = kotlinCompilerPlugins
                .Select(plugin => plugin.Version)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (kotlinCompilerPlugins.Length > 0 && kotlinPluginVersions.Length == 0)
            {
                diagnostics.Add(Diagnostic(
                    "GradleKotlinPluginVersionNotStaticallyResolved",
                    "The Kotlin plugin is present but its compiler version could not be resolved statically.",
                    buildFile));
            }
            else if (kotlinPluginVersions.Length > 1)
            {
                diagnostics.Add(Diagnostic(
                    "GradleKotlinPluginVersionConflict",
                    "The Kotlin plugins declare conflicting compiler versions.",
                    buildFile));
            }

            if (plugins.Any(plugin => plugin.Id == "org.jetbrains.kotlin.multiplatform"))
            {
                diagnostics.Add(Diagnostic(
                    "GradleKotlinMultiplatformTargetNotStaticallyResolved",
                    "Kotlin Multiplatform targets are not treated as JVM source roots until target-specific source-set resolution is available.",
                    buildFile));
            }
            else if (kotlinAndroidPlugins.Length > 0)
            {
                diagnostics.Add(Diagnostic(
                    "GradleKotlinAndroidTargetNotSupported",
                    "Android Kotlin source analysis is not enabled until Android variants, SDK classpaths and source-set ownership are modelled.",
                    buildFile));
            }
            else if (sourceRoots.Any(sourceRoot => sourceRoot.ContainsKotlinSources)
                     && kotlinJvmPlugins.Length == 0)
            {
                diagnostics.Add(Diagnostic(
                    "GradleKotlinJvmPluginNotDetected",
                    "Kotlin source files were discovered, but a statically resolved Kotlin JVM plugin was not found.",
                    buildFile));
            }

            var kotlinPluginVersion = kotlinPluginVersions.Length == 1
                ? kotlinPluginVersions[0]
                : null;
            var kotlinCompilerOptions = ParseKotlinCompilerOptions(
                source, kotlinPluginVersion, diagnostics, buildFile);

            foreach (var plugin in plugins.Where(plugin =>
                         plugin.Id.StartsWith("org.jetbrains.kotlin.plugin.", StringComparison.Ordinal)
                         || plugin.Id is "org.jetbrains.kotlin.kapt" or "com.google.devtools.ksp"))
            {
                diagnostics.Add(Diagnostic(
                    "GradleKotlinCompilerPluginNotExecuted",
                    $"Kotlin compiler plugin '{plugin.Id}' is not executed by Aegis. Plugin-generated or transformed semantics may be incomplete.",
                    buildFile));
            }

            var model = new GradleBuildModel
            {
                BuildFile = buildFile,
                Dialect = Path.GetFileName(buildFile).EndsWith(".kts", StringComparison.OrdinalIgnoreCase)
                    ? "Kotlin"
                    : "Groovy",
                Plugins = plugins,
                Dependencies = dependencies,
                SourceSets = sourceSets,
                TestTasks = testTasks,
                SourceRoots = sourceRoots,
                JavaToolchainVersion = toolchain,
                KotlinPluginVersion = kotlinPluginVersion,
                KotlinLanguageVersion = kotlinCompilerOptions.LanguageVersion,
                KotlinApiVersion = kotlinCompilerOptions.ApiVersion,
                KotlinJvmTarget = kotlinCompilerOptions.JvmTarget,
                Android = androidModel,
                KotlinCompilerConfigurationInferred = kotlinPluginVersion is null
                    || kotlinCompilerOptions.ConfigurationInferred,
                Diagnostics = diagnostics,
                IsComplete = diagnostics.Count == 0
            };
            module.GradleBuildModel = model;
            module.SourceRoots = model.SourceRoots;
            AddDependencyEvidence(context, module, model);
        }
    }

    private static GradleAndroidBuildModel? ParseAndroidBuildModel(
        string source,
        IReadOnlyCollection<GradleBuildModel.Plugin> plugins,
        VersionCatalog catalog,
        IReadOnlyCollection<string> files,
        string modulePath,
        string? owningRootPath,
        IReadOnlyCollection<ProjectSourceRoot> sourceRoots,
        ICollection<AnalysisDiagnostic> diagnostics,
        string buildFile)
    {
        var androidPlugins = plugins
            .Where(plugin => plugin.Id is
                "com.android.application"
                or "com.android.library"
                or "com.android.test"
                or "com.android.dynamic-feature"
                or "com.android.asset-pack")
            .ToArray();
        if (androidPlugins.Length == 0)
            return null;

        var modelDiagnostics = new List<AnalysisDiagnostic>();
        if (androidPlugins.Select(plugin => plugin.Id).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            modelDiagnostics.Add(Diagnostic(
                "GradleAndroidPluginConflict",
                "A module declares more than one mutually exclusive Android Gradle Plugin module type.",
                buildFile));
        }

        var androidPlugin = androidPlugins[0];
        var pluginVersion = androidPlugin.Version
            ?? ResolveRootPluginVersion(androidPlugin.Id, catalog, files, owningRootPath);
        if (string.IsNullOrWhiteSpace(pluginVersion))
        {
            modelDiagnostics.Add(Diagnostic(
                "GradleAndroidPluginVersionNotStaticallyResolved",
                $"Android plugin '{androidPlugin.Id}' is applied, but its version is not statically available from the module or root plugin declaration.",
                buildFile));
        }

        var moduleKind = androidPlugin.Id switch
        {
            "com.android.application" => "Application",
            "com.android.library" => "Library",
            "com.android.test" => "Test",
            "com.android.dynamic-feature" => "DynamicFeature",
            "com.android.asset-pack" => "AssetPack",
            _ => "Unknown"
        };

        var androidBlock = ExtractBlock(source, "android");
        if (androidBlock is null)
        {
            modelDiagnostics.Add(Diagnostic(
                "GradleAndroidConfigurationNotFound",
                "An Android Gradle Plugin is applied, but no statically readable android configuration block was found.",
                buildFile));
        }

        var namespaceValue = ReadAndroidLiteral(androidBlock, "namespace");
        var compileSdk = ReadAndroidLiteral(androidBlock, "compileSdk", "compileSdkVersion");
        var compileSdkPreview = ReadAndroidLiteral(androidBlock, "compileSdkPreview");
        var buildToolsVersion = ReadAndroidLiteral(androidBlock, "buildToolsVersion");

        if (androidBlock is not null && compileSdk is null && compileSdkPreview is null)
        {
            modelDiagnostics.Add(Diagnostic(
                "GradleAndroidCompileSdkNotStaticallyResolved",
                "The Android compile SDK is not expressed as a supported literal; SDK selection may be inherited or dynamic.",
                buildFile));
        }

        var agpMajorVersion = TryGetMajorVersion(pluginVersion);
        if (androidBlock is not null && agpMajorVersion >= 8 && namespaceValue is null)
        {
            modelDiagnostics.Add(Diagnostic(
                "GradleAndroidNamespaceNotStaticallyResolved",
                "The Android namespace is not expressed as a supported literal; namespace ownership cannot be established statically.",
                buildFile));
        }

        var defaultConfigBlock = androidBlock is null ? null : ExtractBlock(androidBlock, "defaultConfig");
        var defaultConfig = defaultConfigBlock is null
            ? null
            : new GradleAndroidBuildModel.DefaultConfig(
                ReadAndroidLiteral(defaultConfigBlock, "applicationId"),
                ReadAndroidLiteral(defaultConfigBlock, "minSdk", "minSdkVersion"),
                ReadAndroidLiteral(defaultConfigBlock, "targetSdk", "targetSdkVersion"),
                ReadAndroidLiteral(defaultConfigBlock, "testInstrumentationRunner"));

        var compileOptionsBlock = androidBlock is null ? null : ExtractBlock(androidBlock, "compileOptions");
        var compileOptions = compileOptionsBlock is null
            ? null
            : new GradleAndroidBuildModel.CompileOptions(
                ReadAndroidLiteral(compileOptionsBlock, "sourceCompatibility"),
                ReadAndroidLiteral(compileOptionsBlock, "targetCompatibility"),
                ReadAndroidBoolean(compileOptionsBlock, "isCoreLibraryDesugaringEnabled", "coreLibraryDesugaringEnabled"));

        var flavorDimensions = ReadQuotedValues(androidBlock, "flavorDimensions");
        var productFlavors = ExtractNamedBlocks(
                androidBlock is null ? null : ExtractBlock(androidBlock, "productFlavors"))
            .Select(entry => new GradleAndroidBuildModel.ProductFlavor(
                entry.Name,
                ReadAndroidLiteral(entry.Block, "dimension"),
                ReadAndroidLiteral(entry.Block, "applicationIdSuffix"),
                ReadAndroidLiteral(entry.Block, "versionNameSuffix"),
                ReadAndroidLiteral(entry.Block, "minSdk", "minSdkVersion"),
                ReadAndroidLiteral(entry.Block, "targetSdk", "targetSdkVersion")))
            .ToList();

        var buildTypes = ExtractNamedBlocks(
                androidBlock is null ? null : ExtractBlock(androidBlock, "buildTypes"))
            .Select(entry => new GradleAndroidBuildModel.BuildType(
                entry.Name,
                ReadAndroidBoolean(entry.Block, "isDebuggable", "debuggable"),
                ReadAndroidBoolean(entry.Block, "isMinifyEnabled", "minifyEnabled"),
                ReadAndroidLiteral(entry.Block, "applicationIdSuffix")))
            .ToList();

        var legacyKotlinAndroid = plugins.Any(plugin =>
            plugin.Id.Equals("org.jetbrains.kotlin.android", StringComparison.Ordinal)
            || plugin.Id.Equals("kotlin-android", StringComparison.Ordinal));
        var builtInKotlinPlugin = plugins.Any(plugin =>
            plugin.Id.Equals("com.android.built-in-kotlin", StringComparison.Ordinal));
        var builtInKotlinDisabled = ReadGradleProperty(
            files, modulePath, owningRootPath, "android.builtInKotlin")?.Equals(
                "false", StringComparison.OrdinalIgnoreCase) == true;

        var kotlinIntegration = builtInKotlinPlugin
            ? "BuiltInPlugin"
            : legacyKotlinAndroid
                ? "LegacyKotlinAndroidPlugin"
                : agpMajorVersion >= 9
                    ? builtInKotlinDisabled ? "Disabled" : "BuiltInDefault"
                    : agpMajorVersion > 0 ? "NotDeclared" : "Unknown";

        if (legacyKotlinAndroid && agpMajorVersion >= 9 && !builtInKotlinDisabled)
        {
            modelDiagnostics.Add(Diagnostic(
                "GradleAndroidLegacyKotlinPluginConflictsWithBuiltInKotlin",
                "AGP 9 enables built-in Kotlin by default; the legacy org.jetbrains.kotlin.android plugin is incompatible unless built-in Kotlin is explicitly disabled.",
                buildFile));
        }

        if (sourceRoots.Any(root => root.ContainsKotlinSources)
            && kotlinIntegration is "Disabled" or "NotDeclared" or "Unknown")
        {
            modelDiagnostics.Add(Diagnostic(
                "GradleAndroidKotlinIntegrationNotResolved",
                "Kotlin sources are present in this Android module, but the active Kotlin integration could not be established from supported static build evidence.",
                buildFile));
        }

        diagnostics.AddRange(modelDiagnostics);

        return new GradleAndroidBuildModel
        {
            PluginId = androidPlugin.Id,
            PluginVersion = pluginVersion,
            ModuleKind = moduleKind,
            KotlinIntegration = kotlinIntegration,
            Namespace = namespaceValue,
            CompileSdk = compileSdk,
            CompileSdkPreview = compileSdkPreview,
            BuildToolsVersion = buildToolsVersion,
            DefaultConfig = defaultConfig,
            CompileOptions = compileOptions,
            FlavorDimensions = flavorDimensions,
            ProductFlavors = productFlavors,
            BuildTypes = buildTypes,
            Diagnostics = modelDiagnostics,
            IsComplete = modelDiagnostics.Count == 0
        };
    }

    private static string? ResolveRootPluginVersion(
        string pluginId,
        VersionCatalog catalog,
        IReadOnlyCollection<string> files,
        string? owningRootPath)
    {
        if (string.IsNullOrWhiteSpace(owningRootPath))
            return null;

        var rootBuildFile = files.FirstOrDefault(file =>
            IsBuildFile(file)
            && PathsEqual(Path.GetDirectoryName(Path.GetFullPath(file))!, Path.GetFullPath(owningRootPath)));
        if (rootBuildFile is null)
            return null;

        try
        {
            var source = StripComments(File.ReadAllText(rootBuildFile));
            var rootPlugins = ParsePlugins(
                ExtractBlock(source, "plugins"),
                catalog,
                new List<AnalysisDiagnostic>(),
                rootBuildFile);
            return rootPlugins.FirstOrDefault(plugin => plugin.Id.Equals(pluginId, StringComparison.Ordinal))?.Version;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadGradleProperty(
        IReadOnlyCollection<string> files,
        string modulePath,
        string? owningRootPath,
        string propertyName)
    {
        var candidates = new[]
            {
                Path.Combine(modulePath, "gradle.properties"),
                string.IsNullOrWhiteSpace(owningRootPath)
                    ? null
                    : Path.Combine(owningRootPath, "gradle.properties")
            }
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(PathComparer);

        foreach (var candidate in candidates)
        {
            if (!files.Any(file => PathsEqual(file, candidate)))
                continue;

            try
            {
                foreach (var line in File.ReadLines(candidate))
                {
                    var match = Regex.Match(
                        line,
                        @"^\s*" + Regex.Escape(propertyName) + @"\s*=\s*(?<value>.*?)\s*$",
                        RegexOptions.CultureInvariant);
                    if (match.Success)
                        return match.Groups["value"].Value.Trim();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static int TryGetMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return 0;

        var match = Regex.Match(version, @"^\s*(?<major>\d+)(?:\.|$)", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["major"].Value, out var major)
            ? major
            : 0;
    }

    private static string? ReadAndroidLiteral(string? block, params string[] names)
    {
        if (block is null)
            return null;

        foreach (var name in names)
        {
            var match = Regex.Match(
                block,
                @"\b" + Regex.Escape(name)
                + @"\b\s*(?:=\s*|\(\s*|\s+)(?<value>['""](?<quoted>[^'""]+)['""]|(?<token>\d+(?:\.\d+)*|[A-Z][A-Za-z0-9_.-]*))\s*\)?",
                RegexOptions.CultureInvariant);
            if (match.Success)
                return match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["token"].Value;
        }

        return null;
    }

    private static bool? ReadAndroidBoolean(string? block, params string[] names)
    {
        if (block is null)
            return null;

        foreach (var name in names)
        {
            var match = Regex.Match(
                block,
                @"\b" + Regex.Escape(name) + @"\b\s*(?:=\s*|\s+)(?<value>true|false)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success && bool.TryParse(match.Groups["value"].Value, out var value))
                return value;
        }

        return null;
    }

    private static List<string> ReadQuotedValues(string? block, string propertyName)
    {
        if (block is null)
            return [];

        var match = Regex.Match(
            block,
            @"\b" + Regex.Escape(propertyName) + @"\b[^\r\n]*",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return [];

        return Regex.Matches(match.Value, @"['""](?<value>[^'""]+)['""]")
            .Cast<Match>()
            .Select(value => value.Groups["value"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<NamedBlock> ExtractNamedBlocks(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return [];

        var result = new List<NamedBlock>();
        var declaration = new Regex(
            @"(?:create|getByName|named)\s*\(\s*['""](?<factory>[^'""]+)['""]\s*\)\s*\{|(?<named>[A-Za-z_][A-Za-z0-9_]*)\s*\{",
            RegexOptions.CultureInvariant);
        var offset = 0;

        while (offset < source.Length)
        {
            var match = declaration.Match(source, offset);
            if (!match.Success)
                break;

            var opening = source.IndexOf('{', match.Index);
            var block = ExtractBlockAt(source, opening, out var closing);
            if (block is null)
            {
                offset = opening + 1;
                continue;
            }

            var name = match.Groups["factory"].Success
                ? match.Groups["factory"].Value
                : match.Groups["named"].Value;
            result.Add(new NamedBlock(name, block));
            offset = closing + 1;
        }

        return result;
    }

    private static string? ExtractBlockAt(string source, int opening, out int closing)
    {
        closing = -1;
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
            else if (current == '{')
            {
                depth++;
            }
            else if (current == '}' && --depth == 0)
            {
                closing = index;
                return source[(opening + 1)..index];
            }
        }

        return null;
    }

    private sealed record NamedBlock(string Name, string Block);

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

            var kotlinDslMatch = KotlinDslPluginDeclaration.Match(line);
            if (kotlinDslMatch.Success)
            {
                var version = kotlinDslMatch.Groups["version"].Success
                    ? kotlinDslMatch.Groups["version"].Value
                    : null;
                if (line.Contains("version", StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(version))
                {
                    diagnostics.Add(Diagnostic(
                        "GradleKotlinPluginVersionNotStaticallyResolved",
                        "The Kotlin Gradle plugin version uses a dynamic or unsupported expression.",
                        buildFile));
                }

                result.Add(new GradleBuildModel.Plugin(
                    "org.jetbrains.kotlin." + kotlinDslMatch.Groups["plugin"].Value,
                    version,
                    true));
            }
            else
            {
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
                         || line.Contains("alias(", StringComparison.Ordinal)
                         || line.Contains("kotlin(", StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic("GradlePluginDeclarationNotStatic",
                        "A plugin declaration uses an unsupported or dynamic expression.", buildFile));
                }
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

        var constraintBlockDepth = 0;
        foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (constraintBlockDepth > 0)
            {
                constraintBlockDepth += CountBraceDelta(line);
                continue;
            }

            if (Regex.IsMatch(line, @"^\s*constraints\s*\{", RegexOptions.CultureInvariant))
            {
                constraintBlockDepth = CountBraceDelta(line);
                diagnostics.Add(Diagnostic(
                    "GradleDependencyConstraintsNotModeled",
                    "Gradle dependency constraints are not yet represented as first-class dependency evidence; declarations inside the constraints block are intentionally excluded from direct dependencies.",
                    buildFile));
                continue;
            }

            var match = DependencyDeclaration.Match(line);
            if (!match.Success)
                continue;

            var configuration = match.Groups["configuration"].Value;
            var argument = match.Groups["argument"].Success
                ? match.Groups["argument"].Value.Trim()
                : match.Groups["groovy"].Value.Trim();
            if (configuration.Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Diagnostic(
                    "GradleDependencyDeclarationNotSupported",
                    $"Dependency declaration '{line.Trim()}' uses the dynamic add(...) form, which is outside the supported static subset.",
                    buildFile));
                continue;
            }

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

            var bundleAlias = Regex.Match(
                argument, @"^libs\.bundles\.(?<alias>[A-Za-z0-9_.-]+)$",
                RegexOptions.CultureInvariant);
            if (bundleAlias.Success)
            {
                var bundleKey = NormalizeAlias(bundleAlias.Groups["alias"].Value);
                if (!catalog.Bundles.TryGetValue(bundleKey, out var members))
                {
                    diagnostics.Add(Diagnostic(
                        "GradleDependencyBundleNotResolved",
                        $"Dependency bundle alias '{bundleKey}' is not present in the discovered version catalog.",
                        buildFile));
                    continue;
                }

                foreach (var member in members)
                {
                    if (!catalog.Libraries.TryGetValue(member, out var library))
                    {
                        diagnostics.Add(Diagnostic(
                            "GradleDependencyAliasNotResolved",
                            $"Library alias '{member}' referenced by bundle '{bundleKey}' is not present in the discovered version catalog.",
                            buildFile));
                        continue;
                    }

                    result.Add(new GradleBuildModel.Dependency(
                        configuration, library.Group, library.Artifact, library.Version,
                        null, member, isPlatform, isEnforcedPlatform, isAnnotationProcessor, line.Trim()));
                }

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

    private static int CountBraceDelta(string line)
    {
        var delta = 0;
        var inString = false;
        var escaped = false;
        var quote = '\0';

        foreach (var character in line)
        {
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == quote)
                    inString = false;
                continue;
            }

            if (character is '\'' or '"')
            {
                inString = true;
                quote = character;
            }
            else if (character == '{')
            {
                delta++;
            }
            else if (character == '}')
            {
                delta--;
            }
        }

        return delta;
    }

    private static void AddDependencyEvidence(
        ProjectArchitectureContext context,
        ProjectModuleContext module,
        GradleBuildModel model)
    {
        foreach (var dependency in model.Dependencies)
        {
            var testOnly = IsTestConfiguration(dependency.Configuration);
            var isProjectDependency = !string.IsNullOrWhiteSpace(dependency.ProjectPath);
            var hasPackageCoordinates = !string.IsNullOrWhiteSpace(dependency.Group)
                && !string.IsNullOrWhiteSpace(dependency.Artifact);

            if (!isProjectDependency && !hasPackageCoordinates)
                continue;

            var name = isProjectDependency
                ? $"project:{dependency.ProjectPath}"
                : $"{dependency.Group}:{dependency.Artifact}";
            var identity = isProjectDependency
                ? $"gradle:project:{dependency.ProjectPath}:{dependency.Configuration}:{module.Path}"
                : $"gradle:{dependency.Group}:{dependency.Artifact}:{dependency.Version}:{dependency.Configuration}:{module.Path}";

            context.DependencyEvidence.Add(new ArchitectureDependencyEvidence
            {
                Id = identity,
                Name = name,
                Ecosystem = "Gradle",
                Kind = isProjectDependency
                    ? DependencyEvidenceKind.Project
                    : dependency.IsPlatform ? DependencyEvidenceKind.Platform : DependencyEvidenceKind.Package,
                DeclaredVersion = dependency.Version,
                ResolvedVersion = null,
                Scope = dependency.Configuration,
                SourceProjectPath = module.Path,
                IsDirect = true,
                IsTestOnly = testOnly,
                IsEnforced = dependency.IsEnforcedPlatform,
                ResolutionStatus = isProjectDependency
                    ? DependencyResolutionStatus.Declared
                    : string.IsNullOrWhiteSpace(dependency.Version)
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

    private static bool IsTestConfiguration(string configuration)
    {
        return Regex.Split(configuration, @"(?<=[a-z0-9])(?=[A-Z])")
            .Any(segment => segment.Equals("test", StringComparison.OrdinalIgnoreCase));
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
            var bundles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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
                        if (section is "versions" or "libraries" or "bundles" or "plugins")
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
                        var resolvedVersion = ReadCatalogVersion(value);
                        if (resolvedVersion is not null)
                        {
                            versions[alias] = resolvedVersion;
                        }
                        else
                        {
                            diagnostics.Add(Diagnostic(
                                "GradleVersionCatalogVersionNotStatic",
                                $"Version alias '{alias}' does not contain a supported literal version or rich version constraint.",
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
                    else if (section.Equals("bundles", StringComparison.OrdinalIgnoreCase))
                    {
                        var members = Regex.Matches(value, @"['""](?<alias>[^'""]+)['""]")
                            .Cast<Match>()
                            .Select(match => NormalizeAlias(match.Groups["alias"].Value))
                            .ToList();
                        var remainder = string.Concat(
                            Regex.Replace(value, @"['""][^'""]+['""]", string.Empty)
                                .Where(character => !char.IsWhiteSpace(character)
                                    && character is not '[' and not ']' and not ','));
                        if (!value.StartsWith('[') || !value.EndsWith(']')
                            || remainder.Length > 0 || members.Count == 0)
                        {
                            diagnostics.Add(Diagnostic(
                                "GradleVersionCatalogBundleNotStatic",
                                $"Bundle alias '{alias}' does not use a supported literal string array.",
                                file));
                            continue;
                        }

                        bundles[alias] = members;
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

                        var resolvedVersion = ResolveCatalogEntryVersion(
                            value, versions, "Plugin", alias, file, diagnostics);

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

                var resolvedVersion = coordinates.Length == 3
                    ? coordinates[2]
                    : ResolveCatalogEntryVersion(
                        entry.Value, versions, "Library", entry.Key, file, diagnostics);

                result.Libraries[entry.Key] = new VersionCatalog.Library(
                    coordinates[0], coordinates[1], resolvedVersion);
            }

            foreach (var entry in bundles)
            {
                foreach (var member in entry.Value.Where(member => !libraries.ContainsKey(member)))
                {
                    diagnostics.Add(Diagnostic(
                        "GradleVersionCatalogBundleMemberNotResolved",
                        $"Bundle alias '{entry.Key}' references missing library alias '{member}'.",
                        file));
                }

                result.Bundles[entry.Key] = entry.Value;
            }

            foreach (var entry in plugins)
                result.Plugins[entry.Key] = entry.Value;
        }

        return result;
    }

    private static string? ResolveCatalogEntryVersion(
        string value,
        IReadOnlyDictionary<string, string> versions,
        string entryKind,
        string alias,
        string file,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var versionReference = CatalogVersionReference.Match(value);
        if (versionReference.Success)
        {
            var reference = NormalizeAlias(versionReference.Groups["reference"].Value);
            if (versions.TryGetValue(reference, out var referencedVersion))
                return referencedVersion;

            diagnostics.Add(Diagnostic(
                "GradleVersionCatalogVersionReferenceNotResolved",
                entryKind + " alias '" + alias + "' references missing version alias '" + reference + "'.",
                file));
            return null;
        }

        var directVersion = CatalogVersion.Match(value);
        if (directVersion.Success)
            return directVersion.Groups["version"].Value;

        var richVersion = Regex.Match(
            value,
            @"version\s*=\s*\{(?<constraint>[^{}]*)\}",
            RegexOptions.CultureInvariant);
        if (richVersion.Success)
        {
            var resolved = ReadCatalogVersion("{" + richVersion.Groups["constraint"].Value + "}");
            if (resolved is not null)
                return resolved;

            diagnostics.Add(Diagnostic(
                "GradleVersionCatalogRichVersionNotStatic",
                entryKind + " alias '" + alias + "' has no supported strictly, require, or prefer literal version.",
                file));
        }

        return null;
    }

    private static string? ReadCatalogVersion(string value)
    {
        var literal = QuotedNotation.Match(value);
        if (literal.Success)
            return literal.Groups["notation"].Value;

        var strict = Regex.Match(value, @"strictly\s*=\s*['""](?<version>[^'""]+)['""]");
        if (strict.Success)
            return strict.Groups["version"].Value;

        var required = Regex.Match(value, @"require\s*=\s*['""](?<version>[^'""]+)['""]");
        if (required.Success)
            return required.Groups["version"].Value;

        var preferred = Regex.Match(value, @"prefer\s*=\s*['""](?<version>[^'""]+)['""]");
        return preferred.Success ? preferred.Groups["version"].Value : null;
    }

    private static List<GradleBuildModel.TestTask> ParseTestTasks(
        string source,
        IReadOnlyCollection<GradleBuildModel.SourceSet> sourceSets)
    {
        var result = new Dictionary<string, GradleBuildModel.TestTask>(StringComparer.OrdinalIgnoreCase);
        var registrations = new[]
        {
            GenericTestTaskRegistration,
            ExplicitTestTaskRegistration,
            GroovyTestTaskRegistration
        };

        foreach (var registration in registrations)
        {
            foreach (Match match in registration.Matches(source))
            {
                var name = match.Groups["name"].Success
                    ? match.Groups["name"].Value.Trim()
                    : string.Empty;
                if (name.Length == 0 || result.ContainsKey(name))
                    continue;

                var sourceSetName = sourceSets.FirstOrDefault(sourceSet =>
                    sourceSet.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Name;
                result.Add(name, new GradleBuildModel.TestTask(
                    name,
                    "Test",
                    sourceSetName,
                    match.Value.Trim()));
            }
        }

        return result.Values
            .OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<GradleBuildModel.SourceSet> ParseExplicitSourceSets(
        string sourceSetBlock,
        string modulePath,
        string analysisRoot,
        string buildFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var result = new List<GradleBuildModel.SourceSet>();
        var declaration = new Regex(
            @"(?:create\s*\(\s*['""](?<created>[^'""]+)['""]\s*\)|(?:val\s+)?(?<kotlin>[A-Za-z][A-Za-z0-9_]*)\s+by\s+(?:creating|getting)|(?<named>(?<![A-Za-z0-9_])(?!java\b|kotlin\b)[A-Za-z][A-Za-z0-9_]*))\s*\{",
            RegexOptions.CultureInvariant);
        var matches = declaration.Matches(sourceSetBlock);

        foreach (Match match in matches)
        {
            var name = match.Groups["created"].Success
                ? match.Groups["created"].Value
                : match.Groups["kotlin"].Success
                    ? match.Groups["kotlin"].Value
                    : match.Groups["named"].Value;
            var opening = sourceSetBlock.IndexOf('{', match.Index);
            var body = ExtractBlockAt(sourceSetBlock, opening);
            if (body is null)
                continue;

            var statements = new List<string>();
            var javaBlockMatch = Regex.Match(body, @"\bjava\s*\{", RegexOptions.CultureInvariant);
            if (javaBlockMatch.Success)
            {
                var javaOpening = body.IndexOf('{', javaBlockMatch.Index);
                var javaBody = ExtractBlockAt(body, javaOpening);
                if (javaBody is not null)
                {
                    statements.AddRange(javaBody.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                }
                else
                {
                    diagnostics.Add(Diagnostic(
                        "GradleCustomSourceSetNotStaticallyResolved",
                        "Java source directories for source set '" + name
                        + "' use a nested block that could not be isolated safely.",
                        buildFile));
                }
            }

            statements.AddRange(body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => Regex.IsMatch(
                    line, @"\bjava\s*\.\s*(?:srcDir|srcDirs|setSrcDirs)\b",
                    RegexOptions.CultureInvariant)));

            // Only parse Groovy file(...) notation from statements already
            // identified as Java-owned. Scanning the complete source-set body
            // would also capture nested kotlin/resources srcDir declarations.
            var javaStatements = string.Join('\n', statements);
            foreach (Match fileNotation in Regex.Matches(
                         javaStatements,
                         @"(?:\bjava\s*\.\s*)?srcDir\s+(?:file|files)\s*\(\s*['""](?<path>[^'""]+)['""]\s*\)",
                         RegexOptions.CultureInvariant))
            {
                statements.Add("srcDir(\"" + fileNotation.Groups["path"].Value + "\")");
            }

            var directories = new HashSet<string>(PathComparer);
            foreach (var line in statements)
            {
                var hasSourceDirectoryDeclaration = Regex.IsMatch(
                    line, @"(?:\bjava\s*\.\s*)?(?:srcDir|srcDirs|setSrcDirs)\b",
                    RegexOptions.CultureInvariant);
                if (!hasSourceDirectoryDeclaration)
                    continue;

                var quotedValues = Regex.Matches(line, @"['""](?<path>[^'""]*)['""]")
                    .Cast<Match>()
                    .Select(value => value.Groups["path"].Value.Trim())
                    .ToArray();
                var hasDirectLiteralArguments =
                    Regex.IsMatch(
                        line,
                        @"(?:\bjava\s*\.\s*)?(?:srcDir|srcDirs|setSrcDirs)\s*\(\s*['""]",
                        RegexOptions.CultureInvariant)
                    || Regex.IsMatch(
                        line,
                        @"(?:\bjava\s*\.\s*)?(?:srcDirs|setSrcDirs)\s*=\s*(?:\[\s*['""]|(?:listOf|setOf)\s*\(\s*['""]|(?:file|files)\s*\(\s*['""])",
                        RegexOptions.CultureInvariant)
                    || Regex.IsMatch(
                        line,
                        @"(?:\bjava\s*\.\s*)?(?:srcDir|srcDirs|setSrcDirs)\s*(?:\(\s*)?(?:file|files)\s*\(\s*['""]",
                        RegexOptions.CultureInvariant)
                    || Regex.IsMatch(
                        line,
                        @"(?:\bjava\s*\.\s*)?setSrcDirs\s*\(\s*(?:listOf|setOf|files)\s*\(\s*['""]",
                        RegexOptions.CultureInvariant);

                if (!hasDirectLiteralArguments || quotedValues.Length == 0)
                {
                    diagnostics.Add(Diagnostic(
                        "GradleCustomSourceSetNotStaticallyResolved",
                        "Java source directories for source set '" + name
                        + "' use a dynamic or unsupported declaration and were not inferred.",
                        buildFile));
                    continue;
                }

                foreach (var value in quotedValues)
                {
                    if (value.Length == 0 || value.Contains('$') || value.Contains('{'))
                    {
                        diagnostics.Add(Diagnostic(
                            "GradleCustomSourceSetNotStaticallyResolved",
                            "Java source directory '" + value + "' for source set '" + name
                            + "' is dynamic or empty.",
                            buildFile));
                        continue;
                    }

                    string directory;
                    try
                    {
                        directory = Path.GetFullPath(Path.Combine(modulePath, value));
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                        diagnostics.Add(Diagnostic(
                            "GradleCustomSourceSetPathInvalid",
                            "Java source directory '" + value + "' for source set '" + name
                            + "' is invalid: " + exception.Message,
                            buildFile));
                        continue;
                    }

                    if (!IsSameOrDescendantPath(directory, analysisRoot))
                    {
                        diagnostics.Add(Diagnostic(
                            "GradleCustomSourceSetOutsideAnalysisRoot",
                            "Java source directory '" + value + "' for source set '" + name
                            + "' resolves outside the analyzed repository and is not attached.",
                            buildFile));
                        continue;
                    }

                    directories.Add(directory);
                }
            }

            var kotlinDirectories = ParseExplicitKotlinSourceDirectories(
                body, name, modulePath, analysisRoot, buildFile, diagnostics);
            if (directories.Count == 0 && kotlinDirectories.Count == 0)
                continue;

            var orderedDirectories = directories.OrderBy(directory => directory, PathComparer).ToList();
            var orderedKotlinDirectories = kotlinDirectories
                .OrderBy(directory => directory, PathComparer)
                .ToList();
            result.Add(new GradleBuildModel.SourceSet(
                name,
                orderedDirectories,
                IsTestSourceSet(name),
                orderedDirectories,
                orderedKotlinDirectories,
                orderedKotlinDirectories));
        }

        var createDeclarations = Regex.Matches(
            sourceSetBlock, @"\b(?:create|maybeCreate)\s*\(",
            RegexOptions.CultureInvariant).Count;
        if (createDeclarations > matches.Count(match => match.Groups["created"].Success))
        {
            diagnostics.Add(Diagnostic(
                "GradleCustomSourceSetNotStaticallyResolved",
                "One or more source-set declarations are dynamic or outside the supported literal create(name) form.",
                buildFile));
        }

        return result;
    }

    private static List<string> ParseExplicitKotlinSourceDirectories(
        string sourceSetBody,
        string sourceSetName,
        string modulePath,
        string analysisRoot,
        string buildFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var statements = new List<string>();
        var kotlinBlockMatch = Regex.Match(sourceSetBody, @"\bkotlin\s*\{", RegexOptions.CultureInvariant);
        if (kotlinBlockMatch.Success)
        {
            var opening = sourceSetBody.IndexOf('{', kotlinBlockMatch.Index);
            var kotlinBody = ExtractBlockAt(sourceSetBody, opening);
            if (kotlinBody is not null)
                statements.AddRange(kotlinBody.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }

        statements.AddRange(sourceSetBody.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => Regex.IsMatch(
                line, @"\bkotlin\s*\.\s*(?:srcDir|srcDirs|setSrcDirs)\b",
                RegexOptions.CultureInvariant)));

        var directories = new HashSet<string>(PathComparer);
        foreach (var line in statements)
        {
            var hasSourceDirectoryDeclaration = Regex.IsMatch(
                line, @"(?:\bkotlin\s*\.\s*)?(?:srcDir|srcDirs|setSrcDirs)\b",
                RegexOptions.CultureInvariant);
            if (!hasSourceDirectoryDeclaration)
                continue;

            var quotedValues = Regex.Matches(line, @"['""](?<path>[^'""]*)['""]")
                .Cast<Match>()
                .Select(value => value.Groups["path"].Value.Trim())
                .ToArray();
            var hasDirectLiteralArguments =
                Regex.IsMatch(
                    line,
                    @"(?:\bkotlin\s*\.\s*)?(?:srcDir|srcDirs|setSrcDirs)\s*\(\s*['""]",
                    RegexOptions.CultureInvariant)
                || Regex.IsMatch(
                    line,
                    @"(?:\bkotlin\s*\.\s*)?(?:srcDirs|setSrcDirs)\s*=\s*(?:\[\s*['""]|(?:listOf|setOf|files|file)\s*\(\s*['""])",
                    RegexOptions.CultureInvariant)
                || Regex.IsMatch(
                    line,
                    @"(?:\bkotlin\s*\.\s*)?(?:srcDir|srcDirs|setSrcDirs)\s*(?:\(\s*)?(?:file|files)\s*\(\s*['""]",
                    RegexOptions.CultureInvariant)
                || Regex.IsMatch(
                    line,
                    @"(?:\bkotlin\s*\.\s*)?setSrcDirs\s*\(\s*(?:listOf|setOf|files)\s*\(\s*['""]",
                    RegexOptions.CultureInvariant)
                || Regex.IsMatch(
                    line,
                    @"(?:\bkotlin\s*\.\s*)?(?:srcDir|srcDirs)\s+['""]",
                    RegexOptions.CultureInvariant);

            if (!hasDirectLiteralArguments || quotedValues.Length == 0)
            {
                diagnostics.Add(Diagnostic(
                    "GradleCustomSourceSetNotStaticallyResolved",
                    "Kotlin source directories for source set '" + sourceSetName
                    + "' use a dynamic or unsupported declaration and were not inferred.",
                    buildFile));
                continue;
            }

            foreach (var value in quotedValues)
            {
                if (value.Length == 0 || value.Contains('$') || value.Contains('{'))
                {
                    diagnostics.Add(Diagnostic(
                        "GradleCustomSourceSetNotStaticallyResolved",
                        "Kotlin source directory '" + value + "' for source set '" + sourceSetName
                        + "' is dynamic or empty.",
                        buildFile));
                    continue;
                }

                string directory;
                try
                {
                    directory = Path.GetFullPath(Path.Combine(modulePath, value));
                }
                catch (Exception exception) when (
                    exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    diagnostics.Add(Diagnostic(
                        "GradleCustomSourceSetPathInvalid",
                        "Kotlin source directory '" + value + "' for source set '" + sourceSetName
                        + "' is invalid: " + exception.Message,
                        buildFile));
                    continue;
                }

                if (!IsSameOrDescendantPath(directory, analysisRoot))
                {
                    diagnostics.Add(Diagnostic(
                        "GradleCustomSourceSetOutsideAnalysisRoot",
                        "Kotlin source directory '" + value + "' for source set '" + sourceSetName
                        + "' resolves outside the analyzed repository and is not attached.",
                        buildFile));
                    continue;
                }

                directories.Add(directory);
            }
        }

        return directories.ToList();
    }

    private static KotlinCompilerOptions ParseKotlinCompilerOptions(
        string source,
        string? kotlinPluginVersion,
        ICollection<AnalysisDiagnostic> diagnostics,
        string buildFile)
    {
        var languageMatch = KotlinLanguageVersionOption.Match(source);
        var apiMatch = KotlinApiVersionOption.Match(source);
        var jvmTargetMatch = KotlinJvmTargetLiteral.Match(source);
        if (!jvmTargetMatch.Success)
            jvmTargetMatch = KotlinJvmTargetEnum.Match(source);

        if (Regex.IsMatch(source, @"\blanguageVersion\b", RegexOptions.CultureInvariant)
            && !languageMatch.Success)
        {
            diagnostics.Add(Diagnostic(
                "GradleKotlinCompilerOptionNotStaticallyResolved",
                "The Kotlin languageVersion option is declared but is not a supported literal version.",
                buildFile));
        }

        if (Regex.IsMatch(source, @"\bapiVersion\b", RegexOptions.CultureInvariant)
            && !apiMatch.Success)
        {
            diagnostics.Add(Diagnostic(
                "GradleKotlinCompilerOptionNotStaticallyResolved",
                "The Kotlin apiVersion option is declared but is not a supported literal version.",
                buildFile));
        }

        if (Regex.IsMatch(source, @"\bjvmTarget\b", RegexOptions.CultureInvariant)
            && !jvmTargetMatch.Success)
        {
            diagnostics.Add(Diagnostic(
                "GradleKotlinCompilerOptionNotStaticallyResolved",
                "The Kotlin jvmTarget option is declared but is not a supported literal target.",
                buildFile));
        }

        var inferredLanguageVersion = InferKotlinLanguageVersion(kotlinPluginVersion);
        var languageVersion = languageMatch.Success
            ? languageMatch.Groups["version"].Value
            : inferredLanguageVersion;
        var apiVersion = apiMatch.Success
            ? apiMatch.Groups["version"].Value
            : languageVersion;
        var jvmTarget = jvmTargetMatch.Success
            ? jvmTargetMatch.Groups["version"].Value.Replace('_', '.')
            : kotlinPluginVersion is null ? null : "1.8";

        return new KotlinCompilerOptions(
            languageVersion,
            apiVersion,
            jvmTarget,
            !languageMatch.Success || !apiMatch.Success || !jvmTargetMatch.Success);
    }

    private static string? InferKotlinLanguageVersion(string? compilerVersion)
    {
        if (string.IsNullOrWhiteSpace(compilerVersion))
            return null;

        var match = Regex.Match(compilerVersion, @"^(?<major>\d+)\.(?<minor>\d+)");
        return match.Success
            ? match.Groups["major"].Value + "." + match.Groups["minor"].Value
            : null;
    }

    private sealed record KotlinCompilerOptions(
        string? LanguageVersion,
        string? ApiVersion,
        string? JvmTarget,
        bool ConfigurationInferred);

    private static List<GradleBuildModel.SourceSet> MergeSourceSets(
        IReadOnlyCollection<GradleBuildModel.SourceSet> conventional,
        IReadOnlyCollection<GradleBuildModel.SourceSet> explicitSourceSets)
    {
        var result = conventional.ToDictionary(
            sourceSet => sourceSet.Name,
            sourceSet => sourceSet,
            StringComparer.Ordinal);

        foreach (var explicitSourceSet in explicitSourceSets)
        {
            if (result.TryGetValue(explicitSourceSet.Name, out var existing))
            {
                var javaDirectories = existing.JavaSourceDirectories
                    .Concat(explicitSourceSet.JavaSourceDirectories)
                    .Distinct(PathComparer)
                    .ToList();
                var kotlinDirectories = (existing.KotlinSourceDirectories ?? [])
                    .Concat(explicitSourceSet.KotlinSourceDirectories ?? [])
                    .Distinct(PathComparer)
                    .ToList();
                var explicitJavaDirectories = (existing.ExplicitJavaSourceDirectories ?? [])
                    .Concat(explicitSourceSet.ExplicitJavaSourceDirectories ?? [])
                    .Distinct(PathComparer)
                    .ToList();
                var explicitKotlinDirectories = (existing.ExplicitKotlinSourceDirectories ?? [])
                    .Concat(explicitSourceSet.ExplicitKotlinSourceDirectories ?? [])
                    .Distinct(PathComparer)
                    .ToList();

                result[explicitSourceSet.Name] = new GradleBuildModel.SourceSet(
                    existing.Name,
                    javaDirectories,
                    existing.IsTest || explicitSourceSet.IsTest,
                    explicitJavaDirectories,
                    kotlinDirectories,
                    explicitKotlinDirectories);
            }
            else
            {
                result[explicitSourceSet.Name] = explicitSourceSet;
            }
        }

        return result.Values.OrderBy(sourceSet => sourceSet.Name, StringComparer.Ordinal).ToList();
    }

    private static bool IsTestSourceSet(string name) =>
        name.Equals("test", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("test", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
        || name.Equals("testFixtures", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractBlockAt(string source, int opening)
    {
        if (opening < 0 || opening >= source.Length || source[opening] != '{')
            return null;

        var depth = 0;
        var inString = false;
        var escaped = false;
        var quote = '\0';
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

    private static List<ProjectSourceRoot> DiscoverSourceRoots(
        string modulePath,
        IReadOnlyCollection<string> files,
        IReadOnlyCollection<GradleBuildModel.SourceSet> sourceSets,
        string buildFile,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var roots = new Dictionary<string, ProjectSourceRoot>(PathComparer);
        var ownershipConflicts = new HashSet<string>(PathComparer);

        void AddSourceRoot(
            string directory,
            ProjectSourceRootOwnership ownership,
            ProjectSourceRootOrigin origin,
            bool isGenerated = false,
            string? generatedLanguage = null)
        {
            var fullDirectory = Path.GetFullPath(directory);
            var containsJava = string.Equals(generatedLanguage, "Java", StringComparison.OrdinalIgnoreCase)
                || files.Any(file =>
                    Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase)
                    && IsSameOrDescendantPath(file, fullDirectory));
            var containsKotlin = string.Equals(generatedLanguage, "Kotlin", StringComparison.OrdinalIgnoreCase)
                || files.Any(file =>
                    Path.GetExtension(file).Equals(".kt", StringComparison.OrdinalIgnoreCase)
                    && IsSameOrDescendantPath(file, fullDirectory));
            var languages = new List<string>();
            if (containsJava)
                languages.Add("Java");
            if (containsKotlin)
                languages.Add("Kotlin");

            if (roots.TryGetValue(fullDirectory, out var existing))
            {
                if (existing.Origin == ProjectSourceRootOrigin.Explicit
                    && origin != ProjectSourceRootOrigin.Explicit)
                {
                    ownership = ownershipConflicts.Contains(fullDirectory)
                        ? ProjectSourceRootOwnership.Unknown
                        : existing.Ownership;
                }
                else if (existing.Origin != ProjectSourceRootOrigin.Explicit
                    && origin == ProjectSourceRootOrigin.Explicit)
                {
                    // Explicit source-set declarations supersede conventional
                    // folder inference, including conflicts between inferred roots.
                    ownershipConflicts.Remove(fullDirectory);
                }
                else if (ownershipConflicts.Contains(fullDirectory))
                {
                    ownership = ProjectSourceRootOwnership.Unknown;
                }
                else if (existing.Ownership == ProjectSourceRootOwnership.Unknown)
                {
                    // A later source-set declaration can refine an earlier
                    // generic discovery that had no ownership evidence.
                }
                else if (ownership == ProjectSourceRootOwnership.Unknown)
                {
                    ownership = existing.Ownership;
                }
                else if (existing.Ownership != ownership)
                {
                    ownershipConflicts.Add(fullDirectory);
                    diagnostics.Add(Diagnostic(
                        "GradleSourceRootOwnershipConflict",
                        $"Source root '{fullDirectory}' is assigned to conflicting source sets; ownership remains unknown.",
                        buildFile));
                    ownership = ProjectSourceRootOwnership.Unknown;
                }

                languages = existing.Languages
                    .Concat(languages)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                containsJava |= existing.ContainsJavaSources;
                containsKotlin |= existing.ContainsKotlinSources;
                isGenerated |= existing.IsGenerated;
                origin = existing.Origin == ProjectSourceRootOrigin.Explicit
                    || origin == ProjectSourceRootOrigin.Explicit
                    ? ProjectSourceRootOrigin.Explicit
                    : existing.Origin == ProjectSourceRootOrigin.GeneratedConvention
                      || origin == ProjectSourceRootOrigin.GeneratedConvention
                        ? ProjectSourceRootOrigin.GeneratedConvention
                        : ProjectSourceRootOrigin.Conventional;
            }

            roots[fullDirectory] = new ProjectSourceRoot
            {
                Directory = fullDirectory,
                Ownership = ownership,
                Origin = origin,
                IsGenerated = isGenerated,
                ContainsJavaSources = containsJava,
                Languages = languages,
                EvidenceFile = buildFile
            };
        }

        foreach (var sourceSet in sourceSets)
        {
            var ownership = sourceSet.IsTest
                ? ProjectSourceRootOwnership.Test
                : ProjectSourceRootOwnership.Production;

            foreach (var directory in sourceSet.JavaSourceDirectories)
            {
                var fullDirectory = Path.GetFullPath(directory);
                var isExplicit = sourceSet.ExplicitJavaSourceDirectories?.Any(explicitDirectory =>
                    PathsEqual(explicitDirectory, fullDirectory)) == true;
                AddSourceRoot(
                    fullDirectory,
                    ownership,
                    isExplicit ? ProjectSourceRootOrigin.Explicit : ProjectSourceRootOrigin.Conventional);
            }

            foreach (var directory in sourceSet.KotlinSourceDirectories ?? [])
            {
                var fullDirectory = Path.GetFullPath(directory);
                var isExplicit = sourceSet.ExplicitKotlinSourceDirectories?.Any(explicitDirectory =>
                    PathsEqual(explicitDirectory, fullDirectory)) == true;
                AddSourceRoot(
                    fullDirectory,
                    ownership,
                    isExplicit ? ProjectSourceRootOrigin.Explicit : ProjectSourceRootOrigin.Conventional);
            }
        }

        var generatedSourceFiles = DiscoverGeneratedJavaFiles(modulePath, diagnostics);
        foreach (var sourceFile in generatedSourceFiles)
        {
            var relative = Path.GetRelativePath(modulePath, sourceFile)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var buildIndex = 0;
            if (buildIndex + 2 >= relative.Length
                || !relative[buildIndex + 1].Equals("generated", StringComparison.OrdinalIgnoreCase)
                || !relative[buildIndex + 2].Equals("sources", StringComparison.OrdinalIgnoreCase))
                continue;

            var sourceSetIndex = -1;
            // Gradle's generated-source layout places the source-set name
            // immediately after the generator/language segments. Do not scan
            // package names: a package called "main" is not source-set evidence.
            foreach (var candidateIndex in new[] { buildIndex + 5, buildIndex + 4 })
            {
                if (candidateIndex >= relative.Length)
                    continue;

                if (relative[candidateIndex].Equals("main", StringComparison.OrdinalIgnoreCase)
                    || relative[candidateIndex].Equals("test", StringComparison.OrdinalIgnoreCase)
                    || relative[candidateIndex].Equals("integrationTest", StringComparison.OrdinalIgnoreCase)
                    || relative[candidateIndex].Equals("testFixtures", StringComparison.OrdinalIgnoreCase))
                {
                    sourceSetIndex = candidateIndex;
                    break;
                }
            }

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
            AddSourceRoot(
                directory,
                ownership,
                ProjectSourceRootOrigin.GeneratedConvention,
                isGenerated: true,
                generatedLanguage: "Java");
        }

        foreach (var sourceFile in DiscoverGeneratedKotlinFiles(modulePath, diagnostics))
        {
            if (!TryResolveGeneratedKotlinRoot(
                    modulePath, sourceFile, out var directory, out var ownership))
            {
                diagnostics.Add(Diagnostic(
                    "GradleGeneratedKotlinSourceOwnershipUnknown",
                    "Generated Kotlin source '" + sourceFile
                    + "' does not match a supported generated-source layout and remains unclassified.",
                    sourceFile));
                continue;
            }

            if (ownership == ProjectSourceRootOwnership.Unknown)
            {
                diagnostics.Add(Diagnostic(
                    "GradleGeneratedKotlinSourceOwnershipUnknown",
                    "Generated Kotlin source '" + sourceFile
                    + "' is under a generated directory without an identifiable source-set owner.",
                    sourceFile));
            }

            AddSourceRoot(
                directory,
                ownership,
                ProjectSourceRootOrigin.GeneratedConvention,
                isGenerated: true,
                generatedLanguage: "Kotlin");
        }

        return roots.Values
            .OrderBy(root => root.Directory, PathComparer)
            .ToList();
    }

    /// <summary>
    /// Inspects only Gradle's generated Java source subtree. These files remain
    /// excluded from repository-wide governed-file analysis and file counts.
    /// </summary>
    private static IReadOnlyCollection<string> DiscoverGeneratedKotlinFiles(
        string modulePath,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        const int maximumFiles = 50_000;
        const int maximumDepth = 64;
        var generatedDirectories = new[]
        {
            Path.Combine(modulePath, "build", "generated", "ksp"),
            Path.Combine(modulePath, "build", "generated", "source", "kaptKotlin"),
            Path.Combine(modulePath, "build", "generated", "sources")
        };
        var result = new HashSet<string>(PathComparer);

        foreach (var generatedDirectory in generatedDirectories)
        {
            if (!Directory.Exists(generatedDirectory))
                continue;

            var root = Path.GetFullPath(generatedDirectory);
            try
            {
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    continue;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                diagnostics.Add(Diagnostic(
                    "GradleGeneratedKotlinSourceScanFailed",
                    "Generated Kotlin source directory '" + root + "' could not be inspected: " + exception.Message,
                    root));
                continue;
            }

            var pending = new Stack<(string Directory, int Depth)>();
            pending.Push((root, 0));
            while (pending.Count > 0)
            {
                var (currentDirectory, depth) = pending.Pop();
                if (depth > maximumDepth)
                {
                    diagnostics.Add(Diagnostic(
                        "GradleGeneratedKotlinSourceDepthLimitReached",
                        "Generated Kotlin source discovery reached its maximum directory depth.",
                        currentDirectory));
                    continue;
                }

                try
                {
                    foreach (var file in Directory.EnumerateFiles(currentDirectory, "*.kt", SearchOption.TopDirectoryOnly))
                    {
                        var attributes = File.GetAttributes(file);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            continue;

                        var fullPath = Path.GetFullPath(file);
                        if (!IsSameOrDescendantPath(fullPath, root))
                            continue;

                        result.Add(fullPath);
                        if (result.Count >= maximumFiles)
                        {
                            diagnostics.Add(Diagnostic(
                                "GradleGeneratedKotlinSourceLimitReached",
                                "Generated Kotlin source discovery reached its 50,000-file safety limit.",
                                root));
                            return result.OrderBy(path => path, PathComparer).ToArray();
                        }
                    }

                    foreach (var directory in Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly))
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                            continue;

                        pending.Push((directory, depth + 1));
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    diagnostics.Add(Diagnostic(
                        "GradleGeneratedKotlinSourceScanFailed",
                        "Generated Kotlin source directory '" + currentDirectory
                        + "' could not be enumerated: " + exception.Message,
                        currentDirectory));
                }
            }
        }

        return result.OrderBy(path => path, PathComparer).ToArray();
    }

    private static bool TryResolveGeneratedKotlinRoot(
        string modulePath,
        string sourceFile,
        out string root,
        out ProjectSourceRootOwnership ownership)
    {
        var relative = Path.GetRelativePath(modulePath, sourceFile)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        root = string.Empty;
        ownership = ProjectSourceRootOwnership.Unknown;

        if (relative.Length < 4
            || !relative[0].Equals("build", StringComparison.OrdinalIgnoreCase)
            || !relative[1].Equals("generated", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sourceSetIndex = -1;
        var generatorRootLength = 3;
        if (relative[2].Equals("ksp", StringComparison.OrdinalIgnoreCase))
        {
            sourceSetIndex = 3;
        }
        else if (relative.Length >= 5
                 && relative[2].Equals("source", StringComparison.OrdinalIgnoreCase)
                 && relative[3].Equals("kaptKotlin", StringComparison.OrdinalIgnoreCase))
        {
            sourceSetIndex = 4;
            generatorRootLength = 4;
        }
        else if (relative[2].Equals("sources", StringComparison.OrdinalIgnoreCase))
        {
            generatorRootLength = 3;
            foreach (var candidateIndex in new[] { 5, 4 })
            {
                if (candidateIndex < relative.Length
                    && IsKnownGeneratedSourceSet(relative[candidateIndex]))
                {
                    sourceSetIndex = candidateIndex;
                    break;
                }
            }
        }
        else
        {
            return false;
        }

        if (sourceSetIndex >= 0 && sourceSetIndex < relative.Length
            && IsKnownGeneratedSourceSet(relative[sourceSetIndex]))
        {
            ownership = relative[sourceSetIndex].Equals("main", StringComparison.OrdinalIgnoreCase)
                ? ProjectSourceRootOwnership.Production
                : ProjectSourceRootOwnership.Test;
            var rootLength = sourceSetIndex + 1;
            if (rootLength < relative.Length
                && relative[rootLength].Equals("kotlin", StringComparison.OrdinalIgnoreCase))
            {
                rootLength++;
            }

            root = Path.GetFullPath(Path.Combine(
                modulePath,
                Path.Combine(relative.Take(rootLength).ToArray())));
            return IsSameOrDescendantPath(root, modulePath);
        }

        root = Path.GetFullPath(Path.Combine(
            modulePath,
            Path.Combine(relative.Take(Math.Min(relative.Length, generatorRootLength)).ToArray())));
        return IsSameOrDescendantPath(root, modulePath);
    }

    private static bool IsKnownGeneratedSourceSet(string sourceSetName) =>
        sourceSetName.Equals("main", StringComparison.OrdinalIgnoreCase)
        || sourceSetName.Equals("test", StringComparison.OrdinalIgnoreCase)
        || sourceSetName.Equals("integrationTest", StringComparison.OrdinalIgnoreCase)
        || sourceSetName.Equals("testFixtures", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyCollection<string> DiscoverGeneratedJavaFiles(
        string modulePath,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var generatedSourcesDirectory = Path.GetFullPath(Path.Combine(
            modulePath, "build", "generated", "sources"));
        try
        {
            if ((File.GetAttributes(generatedSourcesDirectory) & FileAttributes.ReparsePoint) != 0)
                return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic(
                "GradleGeneratedSourceDiscoveryFailed",
                $"Generated Java source discovery could not inspect '{generatedSourcesDirectory}': {exception.Message}",
                generatedSourcesDirectory));
            return [];
        }

        var discoveredFiles = new List<string>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(generatedSourcesDirectory);

        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    continue;

                foreach (var file in Directory.EnumerateFiles(
                             directory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!Path.GetExtension(file).Equals(
                            ".java", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fullPath = Path.GetFullPath(file);
                    if (!IsSameOrDescendantPath(fullPath, generatedSourcesDirectory))
                        continue;

                    if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) == 0)
                        discoveredFiles.Add(fullPath);
                }

                foreach (var child in Directory.EnumerateDirectories(
                             directory, "*", SearchOption.TopDirectoryOnly))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        pendingDirectories.Push(child);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic(
                    "GradleGeneratedSourceDiscoveryFailed",
                    $"Generated Java source discovery could not inspect '{directory}': {exception.Message}",
                    directory));
            }
        }

        return discoveredFiles;
    }

    private static List<GradleBuildModel.SourceSet> DiscoverConventionalSourceSets(
        string modulePath,
        IReadOnlyCollection<string> files)
    {
        var result = new List<GradleBuildModel.SourceSet>();
        foreach (var sourceSet in new[] { "main", "test", "integrationTest", "testFixtures" })
        {
            var javaDirectory = Path.Combine(modulePath, "src", sourceSet, "java");
            var kotlinDirectory = Path.Combine(modulePath, "src", sourceSet, "kotlin");
            var javaSources = files.Any(file =>
                Path.GetExtension(file).Equals(".java", StringComparison.OrdinalIgnoreCase)
                && IsSameOrDescendantPath(file, javaDirectory));
            var kotlinSources = files.Any(file =>
                Path.GetExtension(file).Equals(".kt", StringComparison.OrdinalIgnoreCase)
                && IsSameOrDescendantPath(file, kotlinDirectory));

            if (!javaSources && !kotlinSources)
                continue;

            result.Add(new GradleBuildModel.SourceSet(
                sourceSet,
                javaSources ? [javaDirectory] : [],
                sourceSet != "main",
                null,
                kotlinSources ? [kotlinDirectory] : [],
                null));
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
        public Dictionary<string, List<string>> Bundles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public sealed record Library(string Group, string Artifact, string? Version);
        public sealed record Plugin(string Id, string? Version);
    }
}
