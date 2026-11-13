using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

/// <summary>
/// Projects compiler-resolved Java symbol relationships onto the existing
/// project dependency graph. Manifest edges remain independent evidence.
/// </summary>
internal static class JavaSemanticDependencyDetector
{
    private const string DependencyType = "JavaSemanticDependency";

    private static readonly HashSet<SemanticRelationshipKind> ProjectRelationshipKinds =
    [
        SemanticRelationshipKind.References,
        SemanticRelationshipKind.Calls,
        SemanticRelationshipKind.Inherits,
        SemanticRelationshipKind.Implements,
        SemanticRelationshipKind.DependsOn,
        SemanticRelationshipKind.Injects,
        SemanticRelationshipKind.Registers,
        SemanticRelationshipKind.LoadsDynamically
    ];

    public static void Analyze(ProjectArchitectureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Dependencies.RemoveAll(dependency =>
            dependency.DependencyType.Equals(DependencyType, StringComparison.OrdinalIgnoreCase));

        var modulesByPath = context.Modules
            .GroupBy(module => NormalizePath(module.Path), PathComparer)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), PathComparer);

        var symbolOwners = context.SemanticAnalysisRuns
            .Where(IsUsableJavaRun)
            .SelectMany(run => run.Symbols
                .Where(IsProjectSourceSymbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Id))
                .Select(symbol => new
                {
                    SymbolId = symbol.Id,
                    ProjectPath = NormalizePath(run.ProjectPath)
                }))
            .GroupBy(item => item.SymbolId, StringComparer.Ordinal)
            .Select(group => new
            {
                SymbolId = group.Key,
                ProjectPaths = group.Select(item => item.ProjectPath)
                    .Distinct(PathComparer)
                    .ToArray()
            })
            .Where(item => item.ProjectPaths.Length == 1)
            .ToDictionary(item => item.SymbolId, item => item.ProjectPaths[0], StringComparer.Ordinal);

        foreach (var run in context.SemanticAnalysisRuns.Where(IsUsableJavaRun))
        {
            if (!modulesByPath.TryGetValue(NormalizePath(run.ProjectPath), out var sourceModule))
                continue;

            var sourceSymbolIds = run.Symbols
                .Where(IsProjectSourceSymbol)
                .Select(symbol => symbol.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var relationship in run.Relationships.Where(relationship =>
                         ProjectRelationshipKinds.Contains(relationship.Kind)
                         && relationship.ResolutionStatus == SemanticResolutionStatus.Resolved
                         && relationship.Provenance.Any(provenance => provenance.Source == SemanticEvidenceSource.Compiler)
                         && sourceSymbolIds.Contains(relationship.SourceSymbolId)
                         && !string.IsNullOrWhiteSpace(relationship.TargetSymbolId)))
            {
                if (!symbolOwners.TryGetValue(relationship.TargetSymbolId!, out var targetPath)
                    || !modulesByPath.TryGetValue(targetPath, out var targetModule)
                    || PathsEqual(sourceModule.Path, targetModule.Path))
                    continue;

                var isTest = relationship.IsTestCode
                    || sourceModule.IsTestProject
                    || sourceModule.IsTestSupport
                    || targetModule.IsTestProject
                    || targetModule.IsTestSupport;
                var sourceFile = relationship.Provenance
                    .Select(provenance => provenance.FilePath)
                    .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

                context.Dependencies.Add(new ArchitectureDependencyContext
                {
                    Source = sourceModule.Name,
                    SourceLayer = sourceModule.Layers.FirstOrDefault()?.Name
                        ?? sourceModule.Type
                        ?? "Unclassified",
                    SourceProjectPath = Path.GetFullPath(sourceModule.Path),
                    Target = targetModule.Name,
                    TargetLayer = targetModule.Layers.FirstOrDefault()?.Name
                        ?? targetModule.Type
                        ?? "Unclassified",
                    TargetProjectPath = Path.GetFullPath(targetModule.Path),
                    DependencyType = DependencyType,
                    Scope = isTest ? "Test" : "Production",
                    File = string.IsNullOrWhiteSpace(sourceFile)
                        ? string.Empty
                        : Path.GetFullPath(sourceFile)
                });
            }
        }

        var unique = context.Dependencies
            .GroupBy(
                dependency => string.Join(
                    "|",
                    dependency.DependencyType,
                    dependency.SourceProjectPath,
                    dependency.TargetProjectPath,
                    dependency.Scope,
                    dependency.File),
                PathComparer)
            .Select(group => group.First())
            .ToArray();

        context.Dependencies = unique.ToList();
    }

    private static bool IsUsableJavaRun(SemanticAnalysisRun run) =>
        run.Language.Equals("Java", StringComparison.OrdinalIgnoreCase)
        && run.EngineId.Equals("eclipse-jdt", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(run.ProjectPath)
        && run.Status is SemanticAnalysisStatus.Completed or SemanticAnalysisStatus.PartiallyCompleted;

    private static bool IsProjectSourceSymbol(SemanticSymbol symbol) =>
        symbol.Origin is SemanticSymbolOrigin.Source or SemanticSymbolOrigin.Generated;

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string left, string right) =>
        PathComparer.Equals(NormalizePath(left), NormalizePath(right));

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
