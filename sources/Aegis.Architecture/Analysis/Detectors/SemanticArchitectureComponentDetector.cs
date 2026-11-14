using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Projects provider-resolved semantic relationships onto language-level architectural
/// components. Self-edges retain relationships internal to one code container; this graph remains distinct from build-module edges.
/// </summary>
internal static class SemanticArchitectureComponentDetector
{
    private const string DependencyType = "SemanticComponentDependency";

    private static readonly HashSet<SemanticRelationshipKind> ComponentRelationshipKinds =
    [
        SemanticRelationshipKind.References,
        SemanticRelationshipKind.Calls,
        SemanticRelationshipKind.Inherits,
        SemanticRelationshipKind.Implements,
        SemanticRelationshipKind.DependsOn,
        SemanticRelationshipKind.UsesAnnotation,
        SemanticRelationshipKind.Injects,
        SemanticRelationshipKind.Registers,
        SemanticRelationshipKind.LoadsDynamically
    ];

    public static void Analyze(ProjectArchitectureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var semanticRuns = context.SemanticAnalysisRuns
            .Where(IsUsableSemanticRun)
            .ToArray();
        if (semanticRuns.Length == 0)
            return;

        context.ArchitectureComponentDependencies.RemoveAll(dependency =>
            dependency.DependencyType.Equals(DependencyType, StringComparison.OrdinalIgnoreCase));
        context.ArchitectureComponentInternalDependencies.Clear();

        // Semantic runs are already scoped to a build module. Their ProjectPath
        // is the authoritative owner identity; the discovered module inventory
        // enriches the display name but must not gate package discovery. In
        // particular, duplicate or incomplete module records must not erase a
        // valid language-level component graph.
        var modulesByPath = context.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .GroupBy(module => NormalizePath(module.Path), PathComparer)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                PathComparer);

        ProjectModuleContext ResolveModule(string path, string language)
        {
            var modulePath = NormalizePath(path);
            if (modulesByPath.TryGetValue(modulePath, out var candidates))
            {
                var languageMatch = candidates.FirstOrDefault(module =>
                    module.Languages.Contains(language, StringComparer.OrdinalIgnoreCase));
                var androidMatch = candidates.FirstOrDefault(module =>
                    module.GradleBuildModel?.Android is not null
                    && module.Languages.Contains(language, StringComparer.OrdinalIgnoreCase));
                return androidMatch ?? languageMatch ?? candidates[0];
            }

            var moduleName = Path.GetFileName(modulePath);
            return new ProjectModuleContext
            {
                Name = string.IsNullOrWhiteSpace(moduleName) ? context.ProjectName : moduleName,
                Path = modulePath,
                Languages = [language]
            };
        }

        var semanticLanguages = semanticRuns
            .Select(run => run.Language)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var symbolComponents = semanticRuns
            .SelectMany(run =>
            {
                var module = ResolveModule(run.ProjectPath, run.Language);
                return run.Symbols
                    .Where(IsProjectSourceSymbol)
                    .Where(symbol =>
                        !string.IsNullOrWhiteSpace(symbol.Id)
                        && !string.IsNullOrWhiteSpace(symbol.FilePath)
                        && (!string.IsNullOrWhiteSpace(symbol.ContainerKind)
                            || !string.IsNullOrWhiteSpace(symbol.ContainerName)))
                    .Select(symbol =>
                    {
                        var componentKind = InferComponentKind(run.Language, symbol.ContainerKind);
                        var componentName = componentKind.Equals("Project", StringComparison.OrdinalIgnoreCase)
                            ? module.Name
                            : string.IsNullOrWhiteSpace(symbol.ContainerName)
                                ? "<default>"
                                : symbol.ContainerName.Trim();
                        var modulePath = NormalizePath(run.ProjectPath);
                        var componentId = CreateComponentId(modulePath, run.Language, componentKind, componentName);
                        var sourceOwnership = ResolveAndroidSourceOwnership(module, symbol.FilePath!, run.Language);
                        if (symbol.IsTestCode || module.IsTestProject || module.IsTestSupport)
                            sourceOwnership = sourceOwnership with { Ownership = "Test" };
                        var isTestCode = sourceOwnership.Ownership == "Test";
                        return new SymbolComponent(
                            symbol.Id,
                            componentId,
                            componentName,
                            componentKind,
                            module.Name,
                            modulePath,
                            run.Language,
                            Path.GetFullPath(symbol.FilePath!),
                            isTestCode,
                            sourceOwnership.Ownership,
                            sourceOwnership.SourceSetName,
                            sourceOwnership.Variants,
                            sourceOwnership.ModuleKind,
                            sourceOwnership.TestKind);
                    });
            })
            .GroupBy(item => item.SymbolId, StringComparer.Ordinal)
            .Select(group => new
            {
                SymbolId = group.Key,
                Components = group
                    .GroupBy(item => item.ComponentId, StringComparer.Ordinal)
                    .Select(componentGroup => componentGroup.First())
                    .ToArray()
            })
            .Where(item => item.Components.Length == 1)
            .ToDictionary(item => item.SymbolId, item => item.Components[0], StringComparer.Ordinal);

        var componentNodes = symbolComponents.Values
            .GroupBy(component => component.ComponentId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var productionSymbols = group.Where(item => item.SourceOwnership == "Production").ToArray();
                var testSymbols = group.Where(item => item.SourceOwnership == "Test").ToArray();
                var files = group.Select(item => item.FilePath)
                    .Distinct(PathComparer)
                    .OrderBy(path => path, PathComparer)
                    .ToList();

                return new ArchitectureComponentContext
                {
                    Id = first.ComponentId,
                    Name = GetComponentDisplayName(first.ComponentName),
                    QualifiedName = first.ComponentName,
                    Language = first.Language,
                    ComponentKind = first.ComponentKind,
                    BuildModuleName = first.ModuleName,
                    BuildModulePath = first.ModulePath,
                    AndroidModuleKind = first.AndroidModuleKind,
                    AndroidVariants = group.SelectMany(item => item.AndroidVariants)
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    AndroidSourceSets = group.Select(item => item.SourceSetName)
                        .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    SourceOwnerships = group.Select(item => item.SourceOwnership)
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    AndroidTestKinds = group.Select(item => item.AndroidTestKind)
                        .Where(value => !string.IsNullOrWhiteSpace(value)
                            && !value.Equals(nameof(GradleAndroidTestKind.None), StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    SourceFileCount = files.Count,
                    ProductionSourceFileCount = productionSymbols
                        .Select(item => item.FilePath)
                        .Distinct(PathComparer)
                        .Count(),
                    TestSourceFileCount = testSymbols
                        .Select(item => item.FilePath)
                        .Distinct(PathComparer)
                        .Count(),
                    SymbolCount = group.Select(item => item.SymbolId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    ProductionSymbolCount = productionSymbols
                        .Select(item => item.SymbolId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    TestSymbolCount = testSymbols
                        .Select(item => item.SymbolId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    SourceFiles = files
                };
            })
            .OrderBy(component => component.BuildModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        context.ArchitectureComponents.RemoveAll(component => semanticLanguages.Contains(component.Language));
        context.ArchitectureComponents.AddRange(componentNodes);

        var edges = new Dictionary<EdgeKey, EdgeAccumulator>();

        foreach (var run in semanticRuns)
        {
            var sourceModule = ResolveModule(run.ProjectPath, run.Language);

            var runSymbols = run.Symbols
                .Where(IsProjectSourceSymbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Id))
                .GroupBy(symbol => symbol.Id, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

            foreach (var relationship in run.Relationships.Where(relationship =>
                         ComponentRelationshipKinds.Contains(relationship.Kind)
                         && (relationship.ResolutionStatus is SemanticResolutionStatus.Resolved
                             or SemanticResolutionStatus.PartiallyResolved)
                         && !string.IsNullOrWhiteSpace(relationship.TargetSymbolId)))
            {
                if (!runSymbols.TryGetValue(relationship.SourceSymbolId, out var sourceSymbol)
                    || !symbolComponents.TryGetValue(relationship.SourceSymbolId, out var sourceComponent)
                    || !symbolComponents.TryGetValue(relationship.TargetSymbolId!, out var targetComponent))
                {
                    continue;
                }

                var targetModule = ResolveModule(targetComponent.ModulePath, targetComponent.Language);
                var isTest = relationship.IsTestCode
                    || sourceSymbol.IsTestCode
                    || targetComponent.IsTestCode
                    || sourceModule.IsTestProject
                    || sourceModule.IsTestSupport
                    || targetModule?.IsTestProject == true
                    || targetModule?.IsTestSupport == true;

                var hasUnknownAndroidOwnership = sourceComponent.SourceOwnership == "Unknown"
                    || targetComponent.SourceOwnership == "Unknown";
                var scope = hasUnknownAndroidOwnership
                    ? "Unknown"
                    : isTest ? "Test" : "Production";
                var key = new EdgeKey(sourceComponent.ComponentId, targetComponent.ComponentId, scope);
                if (!edges.TryGetValue(key, out var accumulator))
                {
                    accumulator = new EdgeAccumulator(sourceComponent, targetComponent);
                    edges.Add(key, accumulator);
                }

                accumulator.Add(run.RunId, relationship);
            }
        }

        var projectedEdges = edges
            .Select(pair => pair.Value.ToContext(pair.Key.Scope))
            .OrderBy(edge => edge.SourceComponentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.TargetComponentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.Scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        context.ArchitectureComponentDependencies.AddRange(
            projectedEdges.Where(edge => !edge.IsInternal));
        context.ArchitectureComponentInternalDependencies.AddRange(
            projectedEdges.Where(edge => edge.IsInternal));

        var sourceSymbolCount = semanticRuns.Sum(run => run.Symbols.Count(IsProjectSourceSymbol));
        var componentSymbolCount = semanticRuns.Sum(run => run.Symbols.Count(symbol =>
            IsProjectSourceSymbol(symbol)
                && (!string.IsNullOrWhiteSpace(symbol.ContainerKind)
                    || !string.IsNullOrWhiteSpace(symbol.ContainerName))));
        var candidateRelationshipCount = semanticRuns.Sum(run => run.Relationships.Count(relationship =>
            ComponentRelationshipKinds.Contains(relationship.Kind)
            && (relationship.ResolutionStatus is SemanticResolutionStatus.Resolved
                or SemanticResolutionStatus.PartiallyResolved)
            && !string.IsNullOrWhiteSpace(relationship.TargetSymbolId)));

        if (componentNodes.Count == 0 && sourceSymbolCount > 0)
        {
            foreach (var run in semanticRuns.Where(run =>
                         run.Symbols.Any(IsProjectSourceSymbol)
                         && !run.Diagnostics.Any(diagnostic =>
                             diagnostic.Code == "SemanticArchitectureComponentProjectionEmpty")))
            {
                run.Diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "SemanticArchitectureComponentProjectionEmpty",
                    Component = nameof(SemanticArchitectureComponentDetector),
                    Message = $"Semantic analysis produced {run.Symbols.Count(IsProjectSourceSymbol)} source symbols, but none could be projected into architectural components. Component symbols={componentSymbolCount}; projected components={componentNodes.Count}.",
                    Target = run.ProjectPath,
                    AffectsCoverage = true
                });
            }
        }

        AegisDiagnostics.Report(
            nameof(SemanticArchitectureComponentDetector),
            componentSymbolCount == 0 && sourceSymbolCount > 0 ? DiagnosticLevel.Warning : DiagnosticLevel.Info,
            $"Semantic architectural component projection: runs={semanticRuns.Length}; sourceSymbols={sourceSymbolCount}; componentSymbols={componentSymbolCount}; components={componentNodes.Count}; candidateRelationships={candidateRelationshipCount}; projectedEdges={edges.Count}; productionEdges={context.ProductionComponentDependencies.Count}; testEdges={context.TestComponentDependencies.Count}.");
    }

    private static AndroidSourceOwnership ResolveAndroidSourceOwnership(
        ProjectModuleContext module,
        string filePath,
        string language)
    {
        var android = module.GradleBuildModel?.Android;
        if (android is null)
        {
            var isAndroidModule = string.Equals(module.Framework, "Android", StringComparison.OrdinalIgnoreCase)
                || module.Frameworks.Contains("Android", StringComparer.OrdinalIgnoreCase);
            var ownership = isAndroidModule
                ? "Unknown"
                : module.IsTestProject || module.IsTestSupport ? "Test" : "Production";
            return new AndroidSourceOwnership(ownership, null, [], null, null);
        }

        var normalizedFile = NormalizePath(filePath);
        var matchingSourceSets = android.SourceSets
            .Select(sourceSet => new
            {
                SourceSet = sourceSet,
                Directories = sourceSet.JavaSourceDirectories
                    .Concat(sourceSet.KotlinSourceDirectories)
                    .Concat(sourceSet.GeneratedJavaSourceDirectories)
                    .Concat(sourceSet.GeneratedKotlinSourceDirectories)
                    .Where(directory => !string.IsNullOrWhiteSpace(directory))
                    .Select(NormalizePath)
                    .Distinct(PathComparer)
                    .Where(directory => IsPathWithin(normalizedFile, directory))
                    .OrderByDescending(directory => directory.Length)
                    .ToArray()
            })
            .Where(item => item.Directories.Length > 0)
            .OrderByDescending(item => item.Directories[0].Length)
            .ToArray();

        if (matchingSourceSets.Length == 0
            || matchingSourceSets.Length > 1
            && matchingSourceSets[0].Directories[0].Length == matchingSourceSets[1].Directories[0].Length)
        {
            return new AndroidSourceOwnership("Unknown", null, [], android.ModuleKind, null);
        }

        var match = matchingSourceSets[0].SourceSet;
        return new AndroidSourceOwnership(
            match.Ownership.ToString(),
            match.Name,
            match.AppliesToVariants,
            android.ModuleKind,
            match.TestKind.ToString());
    }

    private static bool IsPathWithin(string filePath, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, filePath);
        return !Path.IsPathRooted(relativePath)
            && relativePath != "."
            && relativePath != ".."
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record AndroidSourceOwnership(
        string Ownership,
        string? SourceSetName,
        IReadOnlyList<string> Variants,
        string? ModuleKind,
        string? TestKind);

    private static string CreateComponentId(string modulePath, string language, string componentKind, string componentName) =>
        $"{language.ToLowerInvariant()}-{componentKind.ToLowerInvariant()}:{modulePath}:{componentName}";

    private static bool IsUsableSemanticRun(SemanticAnalysisRun run) =>
        !string.IsNullOrWhiteSpace(run.Language)
        && !string.IsNullOrWhiteSpace(run.EngineId)
        && !string.IsNullOrWhiteSpace(run.ProjectPath)
        && run.Status is SemanticAnalysisStatus.Completed or SemanticAnalysisStatus.PartiallyCompleted;

    private static bool IsProjectSourceSymbol(SemanticSymbol symbol) =>
        symbol.Origin is SemanticSymbolOrigin.Source or SemanticSymbolOrigin.Generated;

    private static string InferComponentKind(string language, string? providerKind)
    {
        // C# architectural boundaries are build projects, not namespaces.
        // Other providers may supply a more precise language-native container kind.
        if (language.Equals("C#", StringComparison.OrdinalIgnoreCase)
            || language.Equals("CSharp", StringComparison.OrdinalIgnoreCase))
        {
            return "Project";
        }

        if (!string.IsNullOrWhiteSpace(providerKind))
            return providerKind.Trim();

        return language.ToLowerInvariant() switch
        {
            "java" or "kotlin" or "python" => "Package",
            "typescript" or "javascript" => "Module",
            _ => "Module"
        };
    }

    private static string GetComponentDisplayName(string qualifiedName)
    {
        var separator = Math.Max(qualifiedName.LastIndexOf('.'), qualifiedName.LastIndexOf('/'));
        return separator < 0 ? qualifiedName : qualifiedName[(separator + 1)..];
    }

    private static string GetComponentLabel(SymbolComponent component) =>
        component.ComponentName;

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record SymbolComponent(
        string SymbolId,
        string ComponentId,
        string ComponentName,
        string ComponentKind,
        string ModuleName,
        string ModulePath,
        string Language,
        string FilePath,
        bool IsTestCode,
        string SourceOwnership,
        string? SourceSetName,
        IReadOnlyList<string> AndroidVariants,
        string? AndroidModuleKind,
        string? AndroidTestKind);

    private sealed record EdgeKey(string SourceComponentId, string TargetComponentId, string Scope);

    private sealed class EdgeAccumulator
    {
        private readonly SymbolComponent _source;
        private readonly SymbolComponent _target;
        private readonly HashSet<string> _relationshipIds = new(StringComparer.Ordinal);
        private readonly List<SemanticRelationshipReference> _relationshipReferences = [];
        private readonly HashSet<SemanticRelationship> _unreferencedRelationships = [];
        private readonly HashSet<SemanticRelationshipKind> _relationshipKinds = [];
        private readonly HashSet<string> _evidenceFiles = new(PathComparer);
        private int _resolvedRelationshipCount;
        private int _partiallyResolvedRelationshipCount;

        public EdgeAccumulator(SymbolComponent source, SymbolComponent target)
        {
            _source = source;
            _target = target;
        }

        public void Add(string runId, SemanticRelationship relationship)
        {
            if (string.IsNullOrWhiteSpace(relationship.Id))
            {
                if (!_unreferencedRelationships.Add(relationship))
                    return;
            }
            else if (!_relationshipIds.Add(relationship.Id))
            {
                return;
            }
            else
            {
                _relationshipReferences.Add(new SemanticRelationshipReference
                {
                    RunId = runId,
                    RelationshipId = relationship.Id
                });
            }

            _relationshipKinds.Add(relationship.Kind);
            if (relationship.ResolutionStatus == SemanticResolutionStatus.Resolved)
                _resolvedRelationshipCount++;
            else if (relationship.ResolutionStatus == SemanticResolutionStatus.PartiallyResolved)
                _partiallyResolvedRelationshipCount++;

            foreach (var file in relationship.Provenance
                         .Select(provenance => provenance.FilePath)
                         .Where(file => !string.IsNullOrWhiteSpace(file)))
            {
                _evidenceFiles.Add(Path.GetFullPath(file!));
            }
        }

        public ArchitectureComponentDependencyContext ToContext(string scope) => new()
        {
            SourceComponentId = _source.ComponentId,
            SourceComponentName = GetComponentLabel(_source),
            TargetComponentId = _target.ComponentId,
            TargetComponentName = GetComponentLabel(_target),
            Scope = scope,
            DependencyType = DependencyType,
            IsInternal = string.Equals(
                _source.ComponentId,
                _target.ComponentId,
                StringComparison.Ordinal),
            RelationshipCount = _relationshipIds.Count + _unreferencedRelationships.Count,
            ResolvedRelationshipCount = _resolvedRelationshipCount,
            PartiallyResolvedRelationshipCount = _partiallyResolvedRelationshipCount,
            ContributingRelationships = _relationshipReferences
                .OrderBy(reference => reference.RelationshipId, StringComparer.Ordinal)
                .ToList(),
            UnreferencedRelationshipCount = _unreferencedRelationships.Count,
            RelationshipKinds = _relationshipKinds.OrderBy(kind => kind).ToList(),
            EvidenceFiles = _evidenceFiles.OrderBy(file => file, PathComparer).ToList(),
            AndroidVariants = _source.AndroidVariants.Count > 0 && _target.AndroidVariants.Count > 0
                ? _source.AndroidVariants.Intersect(_target.AndroidVariants, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
                : _source.AndroidVariants.Concat(_target.AndroidVariants)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
            AndroidSourceSets = new[] { _source.SourceSetName, _target.SourceSetName }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
            AndroidModuleKinds = new[] { _source.AndroidModuleKind, _target.AndroidModuleKind }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
            AndroidTestKinds = new[] { _source.AndroidTestKind, _target.AndroidTestKind }
                .Where(value => !string.IsNullOrWhiteSpace(value)
                    && !value.Equals(nameof(GradleAndroidTestKind.None), StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }
}
