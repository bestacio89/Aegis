
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.BackEnd;

public sealed class DependencyGraphEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DependencyGraphPolicy _policy;

    public override string Name => "DependencyGraphEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript",
        "JavaScript"
    ];

    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring Boot",
        "Angular",
        "React",
        "Node"
    ];

    public DependencyGraphEvaluator(
        ILogger<DependencyGraphEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.DependencyGraph
            ?? new DependencyGraphPolicy();
    }

    protected override Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var results = new List<ArchitectureEvaluatorResult>();

        if (Context is not { } context)
        {
            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
                results);
        }

        var relations = context.ProductionDependencies.ToList();
        var componentRelations = context.ProductionComponentDependencies;

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Evaluating dependency graph with {relations.Count} build-module relation(s) and {componentRelations.Count} semantic component relation(s).");

        if (relations.Count > 0)
        {
            EvaluateLayerCoupling(results, relations, token);
            token.ThrowIfCancellationRequested();
            EvaluateGraphSummary(results, relations, token);
        }
        else if (context.ProductionComponents.Count > 0)
        {
            EvaluateComponentGraphSummary(results, componentRelations, token);
        }
        else
        {
            EvaluateGraphSummary(results, relations, token);
        }

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Dependency graph evaluation completed with {results.Count} result(s).");

        return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            results);
    }

    private void EvaluateLayerCoupling(
        List<ArchitectureEvaluatorResult> results,
        IReadOnlyCollection<ArchitectureDependencyContext> relations,
        CancellationToken token)
    {
        if (Context is not { } context)
        {
            return;
        }

        var sourceGroups = relations
            .Where(relation =>
                !string.IsNullOrWhiteSpace(relation.SourceLayer))
            .GroupBy(
                relation => relation.SourceLayer,
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in sourceGroups)
        {
            token.ThrowIfCancellationRequested();

            var dependencies = group
                .Select(relation => relation.TargetLayer)
                .Where(layer => !string.IsNullOrWhiteSpace(layer))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(layer => layer, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dependencyCount = dependencies.Count;

            if (dependencyCount <= _policy.MaxDependenciesPerModule)
            {
                continue;
            }

            // This result describes a layer-level aggregate, not a source file.
            var result = CreateAggregateResult(
                "DependencyGraphViolation");

            result.ProjectName = context.ProjectName;
            result.Language = context.Language;
            result.Framework = context.Framework;
            result.Layer = group.Key;
            result.DetectionConfidence = context.Confidence;

            result.Metrics["DependencyCount"] = dependencyCount;
            result.Metrics["RelationCount"] = group.Count();
            result.Metrics["MaxDependenciesThreshold"] =
                _policy.MaxDependenciesPerModule;
            result.Metrics["Violation"] = 1;

            result.Metadata["Rule"] = "DEP001";
            result.Metadata["Layer"] = group.Key;
            result.Metadata["MeasurementMethod"] =
                "Count of distinct non-empty target layers referenced by this source layer.";
            result.Metadata["Dependencies"] = string.Join(
                ", ",
                dependencies.Take(10));
            result.Metadata["DependencyListTruncated"] =
                (dependencies.Count > 10).ToString();

            results.Add(result);
        }
    }

    private void EvaluateComponentGraphSummary(
        List<ArchitectureEvaluatorResult> results,
        IReadOnlyCollection<ArchitectureComponentDependencyContext> relations,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (Context is not { } context)
            return;

        var nodes = context.ProductionComponents
            .Select(component => component.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var nodeSet = nodes.ToHashSet(StringComparer.Ordinal);
        var observedEdges = relations
            .Where(relation => nodeSet.Contains(relation.SourceComponentId)
                && nodeSet.Contains(relation.TargetComponentId))
            .ToArray();

        var distinctEdges = observedEdges
            .Select(relation => (relation.SourceComponentId, relation.TargetComponentId))
            .Distinct()
            .Count();

        var outgoingCounts = nodes
            .Select(node => observedEdges
                .Where(relation => relation.SourceComponentId == node)
                .Select(relation => relation.TargetComponentId)
                .Distinct(StringComparer.Ordinal)
                .Count())
            .ToArray();

        var result = CreateAggregateResult("DependencyGraphSummary");
        result.ProjectName = context.ProjectName;
        result.Language = context.Language;
        result.Framework = context.Framework;
        result.DetectionConfidence = context.Confidence;

        result.Metrics["TotalRelations"] = observedEdges.Sum(relation => relation.RelationshipCount);
        result.Metrics["TotalComponents"] = nodes.Length;
        result.Metrics["DistinctComponentDependencyCount"] = distinctEdges;
        result.Metrics["AverageDependencies"] = outgoingCounts.Length == 0
            ? 0
            : outgoingCounts.Average();

        result.Metadata["GraphLevel"] = "ArchitecturalComponents";
        result.Metadata["GraphHasRelations"] = (observedEdges.Length > 0).ToString().ToLowerInvariant();
        result.Metadata["MeasurementMethod"] =
            "Compiler-resolved semantic relationships aggregated between language-level architectural components; build-module dependencies are reported separately.";
        result.Metadata["GraphNodeCount"] = nodes.Length.ToString();
        result.Metadata["GraphEdgeCount"] = distinctEdges.ToString();
        var javaRuns = context.SemanticAnalysisRuns
            .Where(run => run.Language.Equals("Java", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var analysisComplete = javaRuns.Length > 0 && javaRuns.All(run => run.IsComplete);
        result.Metadata["AnalysisComplete"] = analysisComplete.ToString().ToLowerInvariant();

        if (nodes.Length > 1 && (distinctEdges > 0 || analysisComplete))
        {
            var possibleDirectedEdges = (double)nodes.Length * (nodes.Length - 1);
            result.Metrics["DependencyGraphDensity"] = distinctEdges / possibleDirectedEdges;
            result.Metadata["GraphDensityDefinition"] =
                "Distinct observed directed non-self package/component edges divided by n*(n-1); this measures the observed graph and does not imply exhaustive semantic coverage.";
            result.Metadata["GraphDensityStatus"] = distinctEdges > 0
                ? "MeasuredOnObservedEdges"
                : "MeasuredZeroEdgesWithCompleteAnalysis";
        }
        else
        {
            result.Metadata["GraphDensityStatus"] = nodes.Length < 2
                ? "Unavailable: fewer than two distinct architectural components."
                : "Unavailable: no observed cross-component dependencies and semantic analysis is incomplete.";
        }

        results.Add(result);
    }

    private void EvaluateGraphSummary(
        List<ArchitectureEvaluatorResult> results,
        IReadOnlyCollection<ArchitectureDependencyContext> relations,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (Context is not { } context)
        {
            return;
        }

        var layers = relations
            .SelectMany(relation => new[]
            {
                relation.SourceLayer,
                relation.TargetLayer
            })
            .Where(layer => !string.IsNullOrWhiteSpace(layer))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var validSourceRelations = relations
            .Where(relation =>
                !string.IsNullOrWhiteSpace(relation.SourceLayer))
            .ToList();

        var distinctLayerDependencyCount = validSourceRelations
            .Where(relation =>
                !string.IsNullOrWhiteSpace(relation.TargetLayer))
            .Select(relation => (
                Source: relation.SourceLayer,
                Target: relation.TargetLayer))
            .Distinct(LayerEdgeTupleComparer.Instance)
            .Count();

        var sourceLayerCount = validSourceRelations
            .Select(relation => relation.SourceLayer)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var averageDependencies = CalculateAverageDependencies(
            validSourceRelations);

        var missingSourceLayerCount = relations.Count(
            relation => string.IsNullOrWhiteSpace(relation.SourceLayer));

        var missingTargetLayerCount = relations.Count(
            relation => string.IsNullOrWhiteSpace(relation.TargetLayer));

        var result = CreateAggregateResult(
            "DependencyGraphSummary");

        result.ProjectName = context.ProjectName;
        result.Language = context.Language;
        result.Framework = context.Framework;
        result.DetectionConfidence = context.Confidence;

        result.Metrics["TotalRelations"] = relations.Count;
        result.Metrics["TotalLayers"] = layers.Count;
        result.Metrics["SourceLayerCount"] = sourceLayerCount;
        result.Metrics["DistinctLayerDependencyCount"] =
            distinctLayerDependencyCount;
        result.Metrics["AverageDependencies"] = averageDependencies;
        result.Metrics["RelationsMissingSourceLayer"] =
            missingSourceLayerCount;
        result.Metrics["RelationsMissingTargetLayer"] =
            missingTargetLayerCount;

        result.Metadata["MeasurementMethod"] =
            "Layer-level summary derived from detected dependency relations.";
        result.Metadata["GraphHasRelations"] =
            (relations.Count > 0).ToString();
        result.Metadata["AnalysisComplete"] =
            (relations.Count > 0
             && missingSourceLayerCount == 0
             && missingTargetLayerCount == 0).ToString().ToLowerInvariant();

        // Density is defined over the observed directed component graph:
        // distinct non-self edges / all possible directed non-self edges.
        // Do not emit a score for an empty or incomplete graph, because no
        // observed relationships is not proof of a measured zero-density graph.
        var hasCompleteComponentEdges = relations.Count > 0
            && relations.All(relation =>
                !string.IsNullOrWhiteSpace(relation.Source)
                && !string.IsNullOrWhiteSpace(relation.Target));

        if (hasCompleteComponentEdges)
        {
            var nodes = relations
                .SelectMany(relation => new[] { relation.Source, relation.Target })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (nodes.Length > 1)
            {
                var edges = relations
                    .Where(relation => !string.Equals(
                        relation.Source,
                        relation.Target,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(relation => (relation.Source, relation.Target))
                    .Distinct(ComponentEdgeTupleComparer.Instance)
                    .Count();

                var possibleDirectedEdges = (double)nodes.Length * (nodes.Length - 1);
                result.Metrics["DependencyGraphDensity"] =
                    edges / possibleDirectedEdges;
                result.Metadata["GraphNodeCount"] = nodes.Length.ToString();
                result.Metadata["GraphEdgeCount"] = edges.ToString();
                result.Metadata["GraphDensityDefinition"] =
                    "Distinct observed directed non-self component edges divided by n*(n-1); measured over the discovered graph, not a claim that discovery is exhaustive.";
                result.Metadata["GraphDensityStatus"] = "Measured";
            }
            else
            {
                result.Metadata["GraphDensityStatus"] =
                    "Unavailable: fewer than two distinct graph nodes.";
            }
        }
        else
        {
            result.Metadata["GraphDensityStatus"] =
                "Unavailable: dependency relations are empty or missing source/target identities.";
        }

        results.Add(result);
    }

    private static double CalculateAverageDependencies(
        IReadOnlyCollection<ArchitectureDependencyContext> relations)
    {
        var groups = relations
            .Where(relation =>
                !string.IsNullOrWhiteSpace(relation.SourceLayer))
            .GroupBy(
                relation => relation.SourceLayer,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groups.Count == 0)
        {
            return 0;
        }

        return groups
            .Select(group =>
                group
                    .Select(relation => relation.TargetLayer)
                    .Where(layer => !string.IsNullOrWhiteSpace(layer))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count())
            .Average();
    }

    private ArchitectureEvaluatorResult CreateAggregateResult(
        string resultName)
    {
        var context = Context;

        var result = new ArchitectureEvaluatorResult(Name, resultName)
        {
            ProjectName = context?.ProjectName,
            Language = context?.Language,
            Framework = context?.Framework,
            DetectionConfidence = context?.Confidence ?? 0
        };

        result.Metadata["EvaluationScope"] = ProjectScope;

        return result;
    }

    private sealed class ComponentEdgeTupleComparer
        : IEqualityComparer<(string Source, string Target)>
    {
        public static ComponentEdgeTupleComparer Instance { get; } = new();

        public bool Equals(
            (string Source, string Target) x,
            (string Source, string Target) y) =>
            string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Source, string Target) edge) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(edge.Source),
                StringComparer.OrdinalIgnoreCase.GetHashCode(edge.Target));
    }

    private sealed class LayerEdgeTupleComparer
        : IEqualityComparer<(string Source, string Target)>
    {
        public static LayerEdgeTupleComparer Instance { get; } = new();

        public bool Equals(
            (string Source, string Target) x,
            (string Source, string Target) y)
        {
            return string.Equals(
                       x.Source,
                       y.Source,
                       StringComparison.OrdinalIgnoreCase)
                   && string.Equals(
                       x.Target,
                       y.Target,
                       StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Source, string Target) edge)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(edge.Source),
                StringComparer.OrdinalIgnoreCase.GetHashCode(edge.Target));
        }
    }
}
