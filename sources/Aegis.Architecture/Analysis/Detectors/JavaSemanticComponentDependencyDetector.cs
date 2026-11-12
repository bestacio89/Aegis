using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Projects compiler-resolved Java declaration relationships onto package-level
/// architectural components. This graph remains distinct from build-module edges.
/// </summary>
internal static class JavaSemanticComponentDependencyDetector
{
    private const string DependencyType = "JavaSemanticPackageDependency";

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

        context.ArchitectureComponentDependencies.RemoveAll(dependency =>
            dependency.DependencyType.Equals(DependencyType, StringComparison.OrdinalIgnoreCase));

        var modulesByPath = context.Modules
            .GroupBy(module => NormalizePath(module.Path), PathComparer)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), PathComparer);

        var symbolComponents = context.SemanticAnalysisRuns
            .Where(IsUsableJavaRun)
            .SelectMany(run =>
            {
                if (!modulesByPath.TryGetValue(NormalizePath(run.ProjectPath), out var module))
                    return Enumerable.Empty<SymbolComponent>();

                return run.Symbols
                    .Where(IsProjectSourceSymbol)
                    .Where(symbol =>
                        !string.IsNullOrWhiteSpace(symbol.Id)
                        && string.Equals(symbol.ContainerKind, "Package", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(symbol.FilePath))
                    .Select(symbol =>
                    {
                        var packageName = string.IsNullOrWhiteSpace(symbol.ContainerName)
                            ? "<default>"
                            : symbol.ContainerName.Trim();
                        var modulePath = NormalizePath(module.Path);
                        var componentId = CreateComponentId(modulePath, packageName);
                        return new SymbolComponent(
                            symbol.Id,
                            componentId,
                            packageName,
                            module.Name,
                            modulePath,
                            symbol.Language,
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
                var files = group.Select(item => item.FilePath)
                    .Distinct(PathComparer)
                    .OrderBy(path => path, PathComparer)
                    .ToList();

                return new ArchitectureComponentContext
                {
                    Id = first.ComponentId,
                    Name = first.PackageName,
                    QualifiedName = first.PackageName,
                    Language = first.Language,
                    ComponentKind = "Package",
                    BuildModuleName = first.ModuleName,
                    BuildModulePath = first.ModulePath,
                    SourceFileCount = files.Count,
                    SymbolCount = group.Select(item => item.SymbolId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    SourceFiles = files
                };
            })
            .OrderBy(component => component.BuildModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.QualifiedName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        context.ArchitectureComponents.RemoveAll(component =>
            component.Language.Equals("Java", StringComparison.OrdinalIgnoreCase)
            && component.ComponentKind.Equals("Package", StringComparison.OrdinalIgnoreCase));
        context.ArchitectureComponents.AddRange(componentNodes);

        var edges = new Dictionary<EdgeKey, EdgeAccumulator>();

        foreach (var run in context.SemanticAnalysisRuns.Where(IsUsableJavaRun))
        {
            if (!modulesByPath.TryGetValue(NormalizePath(run.ProjectPath), out var sourceModule))
                continue;

            var runSymbols = run.Symbols
                .Where(IsProjectSourceSymbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Id))
                .GroupBy(symbol => symbol.Id, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

            foreach (var relationship in run.Relationships.Where(relationship =>
                         ComponentRelationshipKinds.Contains(relationship.Kind)
                         && relationship.ResolutionStatus == SemanticResolutionStatus.Resolved
                         && relationship.Provenance.Any(provenance =>
                             provenance.Source == SemanticEvidenceSource.Compiler)
                         && !string.IsNullOrWhiteSpace(relationship.TargetSymbolId)))
            {
                if (!runSymbols.TryGetValue(relationship.SourceSymbolId, out var sourceSymbol)
                    || !symbolComponents.TryGetValue(relationship.SourceSymbolId, out var sourceComponent)
                    || !symbolComponents.TryGetValue(relationship.TargetSymbolId!, out var targetComponent)
                    || string.Equals(sourceComponent.ComponentId, targetComponent.ComponentId, StringComparison.Ordinal))
                {
                    continue;
                }

                var targetModule = modulesByPath.GetValueOrDefault(targetComponent.ModulePath);
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
    }

    private static string CreateComponentId(string modulePath, string packageName) =>
        $"java-package:{modulePath}:{packageName}";

    private static bool IsUsableJavaRun(SemanticAnalysisRun run) =>
        run.Language.Equals("Java", StringComparison.OrdinalIgnoreCase)
        && run.EngineId.Equals("eclipse-jdt", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(run.ProjectPath)
        && run.Status is SemanticAnalysisStatus.Completed or SemanticAnalysisStatus.PartiallyCompleted;

    private static bool IsProjectSourceSymbol(SemanticSymbol symbol) =>
        symbol.Origin is SemanticSymbolOrigin.Source or SemanticSymbolOrigin.Generated;

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record SymbolComponent(
        string SymbolId,
        string ComponentId,
        string PackageName,
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
            SourceComponentName = $"{_source.ModuleName} · {_source.PackageName}",
            TargetComponentId = _target.ComponentId,
            TargetComponentName = $"{_target.ModuleName} · {_target.PackageName}",
            Scope = scope,
            DependencyType = DependencyType,
            RelationshipCount = _relationshipIds.Count,
            RelationshipKinds = _relationshipKinds.OrderBy(kind => kind).ToList(),
            EvidenceFiles = _evidenceFiles.OrderBy(file => file, PathComparer).ToList()
        };
    }
}
