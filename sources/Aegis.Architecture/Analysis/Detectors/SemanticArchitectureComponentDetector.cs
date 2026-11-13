using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Projects provider-resolved semantic relationships onto language-level architectural
/// components. This graph remains distinct from build-module edges.
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
                return languageMatch ?? candidates[0];
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
                        return new SymbolComponent(
                            symbol.Id,
                            componentId,
                            componentName,
                            componentKind,
                            module.Name,
                            modulePath,
                            run.Language,
                            Path.GetFullPath(symbol.FilePath!),
                            symbol.IsTestCode);
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
                var productionSymbols = group.Where(item => !item.IsTestCode).ToArray();
                var testSymbols = group.Where(item => item.IsTestCode).ToArray();
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
                    || !symbolComponents.TryGetValue(relationship.TargetSymbolId!, out var targetComponent)
                    || string.Equals(sourceComponent.ComponentId, targetComponent.ComponentId, StringComparison.Ordinal))
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

                var scope = isTest ? "Test" : "Production";
                var key = new EdgeKey(sourceComponent.ComponentId, targetComponent.ComponentId, scope);
                if (!edges.TryGetValue(key, out var accumulator))
                {
                    accumulator = new EdgeAccumulator(sourceComponent, targetComponent);
                    edges.Add(key, accumulator);
                }

                accumulator.Add(relationship);
            }
        }

        context.ArchitectureComponentDependencies.AddRange(
            edges.Select(pair => pair.Value.ToContext(pair.Key.Scope))
                .OrderBy(edge => edge.SourceComponentName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.TargetComponentName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.Scope, StringComparer.OrdinalIgnoreCase));

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

        AegisDiagnostics.Report(
            nameof(SemanticArchitectureComponentDetector),
            componentSymbolCount == 0 && sourceSymbolCount > 0 ? DiagnosticLevel.Warning : DiagnosticLevel.Info,
            $"Semantic architectural component projection: runs={semanticRuns.Length}; sourceSymbols={sourceSymbolCount}; componentSymbols={componentSymbolCount}; components={componentNodes.Count}; candidateRelationships={candidateRelationshipCount}; projectedEdges={edges.Count}; productionEdges={context.ProductionComponentDependencies.Count}; testEdges={context.TestComponentDependencies.Count}.");
    }

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
        component.ComponentKind.Equals("Project", StringComparison.OrdinalIgnoreCase)
            ? component.ComponentName
            : $"{component.ModuleName} · {component.ComponentName}";

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
        bool IsTestCode);

    private sealed record EdgeKey(string SourceComponentId, string TargetComponentId, string Scope);

    private sealed class EdgeAccumulator
    {
        private readonly SymbolComponent _source;
        private readonly SymbolComponent _target;
        private readonly HashSet<string> _relationshipIds = new(StringComparer.Ordinal);
        private readonly HashSet<SemanticRelationshipKind> _relationshipKinds = [];
        private readonly HashSet<string> _evidenceFiles = new(PathComparer);

        public EdgeAccumulator(SymbolComponent source, SymbolComponent target)
        {
            _source = source;
            _target = target;
        }

        public void Add(SemanticRelationship relationship)
        {
            if (!_relationshipIds.Add(relationship.Id))
                return;

            _relationshipKinds.Add(relationship.Kind);
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
            SourceComponentName = $"{_source.ModuleName} · {_source.ComponentName}",
            TargetComponentId = _target.ComponentId,
            TargetComponentName = $"{_target.ModuleName} · {_target.ComponentName}",
            Scope = scope,
            DependencyType = DependencyType,
            RelationshipCount = _relationshipIds.Count,
            RelationshipKinds = _relationshipKinds.OrderBy(kind => kind).ToList(),
            EvidenceFiles = _evidenceFiles.OrderBy(file => file, PathComparer).ToList()
        };
    }
}
