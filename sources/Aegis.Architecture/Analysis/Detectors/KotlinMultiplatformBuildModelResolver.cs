using System.Text.RegularExpressions;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Parses a bounded literal subset of Kotlin Multiplatform Gradle declarations.
/// Dynamic Gradle expressions are never evaluated.
/// </summary>
internal static class KotlinMultiplatformBuildModelResolver
{
    private static readonly Regex TargetDeclaration = new(
        @"(?m)^\s*(?<name>jvm|androidTarget|androidLibrary|iosArm64|iosSimulatorArm64|iosX64|macosArm64|macosX64|tvosArm64|tvosSimulatorArm64|tvosX64|watchosArm32|watchosArm64|watchosX64|watchosSimulatorArm64|watchosSimulatorX64|linuxX64|linuxArm64|mingwX64|js|wasmJs|wasmWasi)\s*(?:\((?<args>[^)]*)\))?\s*(?:\{|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SourceSetDeclaration = new(
        @"(?m)^\s*(?:val\s+)?(?<name>[A-Za-z][A-Za-z0-9_]*)\s+by\s+(?:getting|creating)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SourceSetAccessorDeclaration = new(
        @"(?m)^\s*(?<name>[A-Za-z][A-Za-z0-9_]*(?:Main|Test))\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DependsOnDeclaration = new(
        @"\bdependsOn\s*\(\s*(?<name>[A-Za-z][A-Za-z0-9_]*)\s*(?:\.get\s*\(\s*\))?\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DirectDependsOnDeclaration = new(
        @"\b(?<source>[A-Za-z][A-Za-z0-9_]*)(?:\.get\s*\(\s*\))?\.dependsOn\s*\(\s*(?<target>[A-Za-z][A-Za-z0-9_]*)(?:\.get\s*\(\s*\))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SourceSetBlock = new(
        @"\bsourceSets\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static KotlinMultiplatformBuildModel Analyze(string source, string buildFile)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildFile);

        var diagnostics = new List<AnalysisDiagnostic>();
        var kotlinBlock = ExtractBlock(source, "kotlin");
        if (kotlinBlock is null)
        {
            diagnostics.Add(CreateDiagnostic(
                "GradleKotlinMultiplatformBlockNotStaticallyResolved",
                "The Kotlin Multiplatform plugin is applied, but its kotlin configuration block could not be located statically.",
                buildFile));
            return new KotlinMultiplatformBuildModel { Diagnostics = diagnostics, IsComplete = false };
        }

        var targets = TargetDeclaration.Matches(kotlinBlock)
            .Select(match =>
            {
                var name = match.Groups["name"].Value;
                var platform = name switch
                {
                    "jvm" => "JVM",
                    "androidTarget" or "androidLibrary" => "Android",
                    "js" => "JS",
                    "wasmJs" => "Wasm",
                    "wasmWasi" => "Wasm",
                    _ when name.StartsWith("ios", StringComparison.Ordinal)
                        || name.StartsWith("macos", StringComparison.Ordinal)
                        || name.StartsWith("tvos", StringComparison.Ordinal)
                        || name.StartsWith("watchos", StringComparison.Ordinal) => "Native",
                    _ when name.StartsWith("linux", StringComparison.Ordinal)
                        || name.StartsWith("mingw", StringComparison.Ordinal) => "Native",
                    _ => "Unknown"
                };
                return new KotlinMultiplatformBuildModel.Target(
                    name, platform, match.Value.Trim());
            })
            .DistinctBy(target => target.Name, StringComparer.Ordinal)
            .ToList();

        var sourceSetsBlock = ExtractNestedBlock(kotlinBlock, SourceSetBlock) ?? string.Empty;
        var sourceSets = new List<KotlinMultiplatformBuildModel.SourceSet>();
        var hierarchyMode = "Unknown";
        var hasDynamicConfiguration = Regex.IsMatch(
            kotlinBlock,
            @"\b(?:targets\s*\.|targetPresets\s*\.|register\s*\(|applyHierarchyTemplate\s*\{)",
            RegexOptions.CultureInvariant);
        {
            var declarations = SourceSetDeclaration.Matches(sourceSetsBlock)
                .Cast<Match>()
                .Concat(SourceSetAccessorDeclaration.Matches(sourceSetsBlock).Cast<Match>())
                .OrderBy(match => match.Index)
                .GroupBy(match => match.Groups["name"].Value, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(match => match.Index)
                .ToArray();
            for (var index = 0; index < declarations.Length; index++)
            {
                var declaration = declarations[index];
                var name = declaration.Groups["name"].Value;
                var segmentEnd = index + 1 < declarations.Length
                    ? declarations[index + 1].Index
                    : sourceSetsBlock.Length;
                var declarationStart = declaration.Index + declaration.Length;
                var declarationSegment = sourceSetsBlock.Substring(
                    declarationStart,
                    segmentEnd - declarationStart);
                var body = ExtractFollowingBlock(declarationSegment, declarationSegment.IndexOf('{'));
                var dependencies = body is null
                    ? Array.Empty<string>()
                    : DependsOnDeclaration.Matches(body)
                        .Select(match => match.Groups["name"].Value)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                var (kind, targetName) = GetSourceSetKind(name, targets);
                sourceSets.Add(new KotlinMultiplatformBuildModel.SourceSet(
                    name, GetOwnership(name), kind, targetName, dependencies, declaration.Value.Trim()));
                if (kind == "Unknown")
                {
                    diagnostics.Add(CreateDiagnostic(
                        "GradleKotlinMultiplatformSourceSetOwnershipUnknown",
                        $"Source set '{name}' could not be classified as common, intermediate or target-specific.",
                        buildFile));
                }
            }

            var directEdges = DirectDependsOnDeclaration.Matches(sourceSetsBlock)
                .Select(edge => (Source: edge.Groups["source"].Value, Target: edge.Groups["target"].Value))
                .ToArray();
            var hasExplicitEdges = directEdges.Length > 0
                || sourceSets.Any(sourceSet => sourceSet.DependsOn.Count > 0);
            var hasCustomIntermediateSets = sourceSets.Any(sourceSet =>
                sourceSet.Kind == "Intermediate"
                && sourceSet.Declaration.StartsWith("val ", StringComparison.Ordinal));
            var explicitlyAppliesDefaultTemplate = Regex.IsMatch(
                kotlinBlock,
                @"\bapplyDefaultHierarchyTemplate\s*\(\s*\)",
                RegexOptions.CultureInvariant);
            var useDefaultHierarchy = explicitlyAppliesDefaultTemplate
                || (!hasExplicitEdges && !hasCustomIntermediateSets);
            hierarchyMode = useDefaultHierarchy ? "Default" : "Manual";

            AddImplicitSourceSet(sourceSets, "commonMain", "Production", "Common", null);
            AddImplicitSourceSet(sourceSets, "commonTest", "Test", "Common", null);

            // The Kotlin Gradle Plugin creates target-specific Main/Test source sets
            // for each statically declared target, even when the build script never accesses them.
            foreach (var target in targets)
            {
                foreach (var (suffix, ownership, commonSourceSet) in new[]
                         {
                             ("Main", "Production", "commonMain"),
                             ("Test", "Test", "commonTest")
                         })
                {
                    var sourceSetName = $"{target.Name}{suffix}";
                    if (sourceSets.Any(sourceSet => sourceSet.Name.Equals(
                            sourceSetName, StringComparison.Ordinal)))
                        continue;

                    sourceSets.Add(new KotlinMultiplatformBuildModel.SourceSet(
                        sourceSetName,
                        ownership,
                        "TargetSpecific",
                        target.Name,
                        Array.Empty<string>(),
                        $"<implicit:{sourceSetName}>"));
                }
            }

            foreach (var edge in directEdges)
            {
                var sourceIndex = sourceSets.FindIndex(sourceSet => sourceSet.Name == edge.Source);
                if (sourceIndex < 0)
                    continue;

                var sourceSet = sourceSets[sourceIndex];
                sourceSets[sourceIndex] = sourceSet with
                {
                    DependsOn = sourceSet.DependsOn
                        .Append(edge.Target)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                };
            }

            if (useDefaultHierarchy)
                ApplyDefaultHierarchyTemplate(sourceSets, targets);

            if (hasDynamicConfiguration)
            {
                diagnostics.Add(CreateDiagnostic(
                    "GradleKotlinMultiplatformDynamicConfigurationNotResolved",
                    "Kotlin Multiplatform target or hierarchy configuration contains dynamic Gradle expressions that cannot be evaluated safely.",
                    buildFile));
            }

            if (sourceSets.Count == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    "GradleKotlinMultiplatformSourceSetDeclarationsNotResolved",
                    "No supported literal Kotlin Multiplatform source-set declarations were found.",
                    buildFile));
            }
        }

        foreach (var sourceSet in sourceSets)
        {
            foreach (var dependency in sourceSet.DependsOn)
            {
                if (!sourceSets.Any(candidate => candidate.Name.Equals(dependency, StringComparison.Ordinal)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "GradleKotlinMultiplatformSourceSetDependencyUnresolved",
                        $"Source set '{sourceSet.Name}' depends on '{dependency}', which was not statically declared.",
                        buildFile));
                }
            }
        }

        if (targets.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                "GradleKotlinMultiplatformTargetsNotStaticallyResolved",
                "No supported literal Kotlin Multiplatform target declarations were found.",
                buildFile));
        }

        foreach (var commonSourceSet in new[] { "commonMain", "commonTest" })
        {
            if (!sourceSets.Any(sourceSet => sourceSet.Name.Equals(
                    commonSourceSet, StringComparison.Ordinal)))
            {
                diagnostics.Add(CreateDiagnostic(
                    "GradleKotlinMultiplatformCommonSourceSetNotResolved",
                    $"The required common source set '{commonSourceSet}' was not statically declared.",
                    buildFile));
            }
        }

        foreach (var target in targets)
        {
            foreach (var suffix in new[] { "Main", "Test" })
            {
                var expectedSourceSet = $"{target.Name}{suffix}";
                if (!sourceSets.Any(sourceSet => sourceSet.Name.Equals(
                        expectedSourceSet, StringComparison.Ordinal)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "GradleKotlinMultiplatformTargetSourceSetNotResolved",
                        $"Target '{target.Name}' is missing its statically declared '{expectedSourceSet}' source set.",
                        buildFile));
                }
            }
        }

        if (HasSourceSetCycle(sourceSets))
        {
            diagnostics.Add(CreateDiagnostic(
                "GradleKotlinMultiplatformSourceSetDependencyCycle",
                "The statically resolved Kotlin Multiplatform source-set dependsOn graph contains a cycle.",
                buildFile));
        }
        else
        {
            foreach (var sourceSet in sourceSets.Where(sourceSet =>
                         sourceSet.Name is not ("commonMain" or "commonTest")))
            {
                var commonTarget = sourceSet.Ownership == "Production" ? "commonMain"
                    : sourceSet.Ownership == "Test" ? "commonTest"
                    : null;
                if (commonTarget is not null
                    && !HasDependencyPath(sourceSet.Name, commonTarget, sourceSets))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "GradleKotlinMultiplatformSourceSetGraphIncomplete",
                        $"Source set '{sourceSet.Name}' has no statically resolved dependency path to '{commonTarget}'.",
                        buildFile));
                }
            }
        }

        return new KotlinMultiplatformBuildModel
        {
            Targets = targets,
            SourceSets = sourceSets,
            Diagnostics = diagnostics,
            HierarchyMode = hierarchyMode,
            HasDynamicConfiguration = hasDynamicConfiguration,
            IsComplete = diagnostics.Count == 0
                && hierarchyMode != "Unknown"
                && !hasDynamicConfiguration
                && targets.All(target => target.Platform != "Unknown")
                && sourceSets.All(sourceSet => sourceSet.Kind != "Unknown")
                && !HasSourceSetCycle(sourceSets)
                && sourceSets.All(sourceSet => sourceSet.DependsOn.All(dependency =>
                    sourceSets.Any(candidate => candidate.Name == dependency)))
                && sourceSets.Where(sourceSet => sourceSet.Name is not ("commonMain" or "commonTest"))
                    .All(sourceSet => sourceSet.Ownership switch
                    {
                        "Production" => HasDependencyPath(sourceSet.Name, "commonMain", sourceSets),
                        "Test" => HasDependencyPath(sourceSet.Name, "commonTest", sourceSets),
                        _ => false
                    })
        };
    }

    private static void AddImplicitSourceSet(
        ICollection<KotlinMultiplatformBuildModel.SourceSet> sourceSets,
        string name,
        string ownership,
        string kind,
        string? targetName)
    {
        if (sourceSets.Any(sourceSet => sourceSet.Name.Equals(name, StringComparison.Ordinal)))
            return;

        sourceSets.Add(new KotlinMultiplatformBuildModel.SourceSet(
            name, ownership, kind, targetName, Array.Empty<string>(), $"<implicit:{name}>"));
    }

    private static void AddDependency(
        IList<KotlinMultiplatformBuildModel.SourceSet> sourceSets,
        string sourceName,
        string dependencyName)
    {
        var index = sourceSets.ToList().FindIndex(sourceSet => sourceSet.Name == sourceName);
        if (index < 0)
            return;

        var sourceSet = sourceSets[index];
        sourceSets[index] = sourceSet with
        {
            DependsOn = sourceSet.DependsOn
                .Append(dependencyName)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void ApplyDefaultHierarchyTemplate(
        IList<KotlinMultiplatformBuildModel.SourceSet> sourceSets,
        IReadOnlyCollection<KotlinMultiplatformBuildModel.Target> targets)
    {
        var nativeTargets = targets.Where(target => target.Platform == "Native").ToArray();
        if (nativeTargets.Length > 0)
        {
            AddImplicitSourceSet(sourceSets, "nativeMain", "Production", "Intermediate", null);
            AddImplicitSourceSet(sourceSets, "nativeTest", "Test", "Intermediate", null);
            AddDependency(sourceSets, "nativeMain", "commonMain");
            AddDependency(sourceSets, "nativeTest", "commonTest");
        }

        var appleFamilies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in nativeTargets)
        {
            var name = target.Name;
            var family = name.StartsWith("ios", StringComparison.Ordinal) ? "ios"
                : name.StartsWith("tvos", StringComparison.Ordinal) ? "tvos"
                : name.StartsWith("watchos", StringComparison.Ordinal) ? "watchos"
                : name.StartsWith("macos", StringComparison.Ordinal) ? "macos"
                : name.StartsWith("linux", StringComparison.Ordinal) ? "linux"
                : name.StartsWith("mingw", StringComparison.Ordinal) ? "mingw"
                : null;
            if (family is null)
                continue;

            AddImplicitSourceSet(sourceSets, $"{family}Main", "Production", "Intermediate", null);
            AddImplicitSourceSet(sourceSets, $"{family}Test", "Test", "Intermediate", null);
            if (family is "ios" or "tvos" or "watchos" or "macos")
                appleFamilies.Add(family);
            else
            {
                AddDependency(sourceSets, $"{family}Main", "nativeMain");
                AddDependency(sourceSets, $"{family}Test", "nativeTest");
            }

            AddDependency(sourceSets, $"{name}Main", $"{family}Main");
            AddDependency(sourceSets, $"{name}Test", $"{family}Test");
        }

        if (appleFamilies.Count > 0)
        {
            AddImplicitSourceSet(sourceSets, "appleMain", "Production", "Intermediate", null);
            AddImplicitSourceSet(sourceSets, "appleTest", "Test", "Intermediate", null);
            AddDependency(sourceSets, "appleMain", "nativeMain");
            AddDependency(sourceSets, "appleTest", "nativeTest");
            foreach (var family in appleFamilies)
            {
                AddDependency(sourceSets, $"{family}Main", "appleMain");
                AddDependency(sourceSets, $"{family}Test", "appleTest");
            }
        }

        foreach (var target in targets.Where(target => target.Platform is "JVM" or "Android" or "JS" or "Wasm"))
        {
            AddDependency(sourceSets, $"{target.Name}Main", "commonMain");
            AddDependency(sourceSets, $"{target.Name}Test", "commonTest");
        }
    }

    private static bool HasDependencyPath(
        string sourceSetName,
        string targetName,
        IReadOnlyCollection<KotlinMultiplatformBuildModel.SourceSet> sourceSets)
    {
        var dependencies = sourceSets
            .GroupBy(sourceSet => sourceSet.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(sourceSet => sourceSet.DependsOn)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string name)
        {
            if (name.Equals(targetName, StringComparison.Ordinal))
                return true;
            if (!visited.Add(name) || !dependencies.TryGetValue(name, out var targets))
                return false;
            return targets.Any(Visit);
        }

        return Visit(sourceSetName);
    }

    private static bool HasSourceSetCycle(
        IReadOnlyCollection<KotlinMultiplatformBuildModel.SourceSet> sourceSets)
    {
        var dependencies = sourceSets
            .GroupBy(sourceSet => sourceSet.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.SelectMany(sourceSet => sourceSet.DependsOn)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string name)
        {
            if (visiting.Contains(name))
                return true;
            if (!visited.Add(name))
                return false;

            visiting.Add(name);
            foreach (var dependency in dependencies[name])
            {
                if (dependencies.ContainsKey(dependency) && Visit(dependency))
                    return true;
            }
            visiting.Remove(name);
            return false;
        }

        return dependencies.Keys.Any(Visit);
    }

    private static (string Kind, string? TargetName) GetSourceSetKind(
        string name,
        IReadOnlyCollection<KotlinMultiplatformBuildModel.Target> targets)
    {
        var suffixIndex = name.EndsWith("Main", StringComparison.Ordinal)
            ? name.Length - "Main".Length
            : name.EndsWith("Test", StringComparison.Ordinal)
                ? name.Length - "Test".Length
                : name.Length;
        var prefix = name[..suffixIndex];
        if (prefix.Equals("common", StringComparison.OrdinalIgnoreCase))
            return ("Common", null);

        var exactTarget = targets.FirstOrDefault(target =>
            target.Name.Equals(prefix, StringComparison.Ordinal));
        if (exactTarget is not null)
            return ("TargetSpecific", exactTarget.Name);

        if (targets.Any(target => target.Name.StartsWith(prefix, StringComparison.Ordinal)
                                  && prefix.Length > 0)
            || prefix is "native" or "apple")
            return ("Intermediate", null);

        return ("Unknown", null);
    }

    private static string GetOwnership(string name)
    {
        if (name.Equals("commonMain", StringComparison.Ordinal)
            || name.EndsWith("Main", StringComparison.Ordinal))
            return "Production";
        if (name.Equals("commonTest", StringComparison.Ordinal)
            || name.EndsWith("Test", StringComparison.Ordinal))
            return "Test";
        return "Unknown";
    }

    private static string? ExtractBlock(string source, string blockName)
    {
        var match = Regex.Match(source,
            $@"\b{Regex.Escape(blockName)}\s*\{{",
            RegexOptions.CultureInvariant);
        return match.Success ? ExtractFollowingBlock(source, match.Index + match.Length - 1) : null;
    }

    private static string? ExtractNestedBlock(string source, Regex declaration)
    {
        var match = declaration.Match(source);
        return match.Success ? ExtractFollowingBlock(source, match.Index + match.Length - 1) : null;
    }

    private static string? ExtractFollowingBlock(string source, int start)
    {
        if (start < 0 || start >= source.Length)
            return null;

        var open = source.IndexOf('{', start);
        if (open < 0)
            return null;
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[(open + 1)..index];
        }
        return null;
    }

    private static AnalysisDiagnostic CreateDiagnostic(string code, string message, string target) => new()
    {
        Code = code,
        Component = nameof(KotlinMultiplatformBuildModelResolver),
        Message = message,
        Target = target,
        AffectsCoverage = true
    };
}
