using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Architecture;

public sealed class CircularDependencyEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly CircularDependencyPolicy _policy;


    public override string Name =>
        "CircularDependencyEvaluator";


    public override string[] SupportedLanguages =>
    [
        "C#",
        "TypeScript",
        "JavaScript",
        "Java"
    ];


    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring Boot",
        "Angular",
        "React",
        "Node"
    ];



    public CircularDependencyEvaluator(
        ILogger<CircularDependencyEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.CircularDependency
            ?? new CircularDependencyPolicy();
    }



    protected override Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();


        if (Context is not { } context)
            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(results);

        var useComponentGraph = context.ProductionComponents.Count > 0;
        var requiredLanguages = context.ProductionModules
            .SelectMany(module => module.Languages)
            .Append(context.Language)
            .Where(language => !string.IsNullOrWhiteSpace(language)
                && !language.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var semanticRuns = context.SemanticAnalysisRuns
            .Where(run => requiredLanguages.Contains(run.Language))
            .ToArray();
        var analyzedLanguages = semanticRuns
            .Select(run => run.Language)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var componentAnalysisComplete = requiredLanguages.Count > 0
            && requiredLanguages.SetEquals(analyzedLanguages)
            && semanticRuns.All(run => run.IsComplete)
            && !context.DiscoveryDiagnostics.Any(diagnostic => diagnostic.AffectsCoverage);

        if (!useComponentGraph && context.ProductionDependencies.Count == 0)
        {
            // Without either a component graph or build-module edges, absence of
            // cycles is not evidence that a cycle check was completed.
            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(results);
        }

        var graph = useComponentGraph
            ? BuildComponentDependencyGraph(context.ProductionComponentDependencies)
            : BuildDependencyGraph(context.ProductionDependencies);

        // Cycles found in partial semantic evidence are still valid findings.
        // A zero-cycle metric, however, is only emitted when component analysis
        // completed; otherwise it would turn missing edges into a false pass.
        var cycles = DetectCycles(graph);



        foreach (var cycle in cycles)
        {
            token.ThrowIfCancellationRequested();
            results.Add(CreateCycleResult(cycle));
        }

        // Emit completion evidence even when the graph is clean. Otherwise, no finding
        // is indistinguishable from an evaluator that never ran.
        var summary = new ArchitectureEvaluatorResult(Name, projectPath)
        {
            Category = nameof(ArchitectureRuleCategory.Architecture),
            Metadata = new Dictionary<string, string>
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["AnalysisCompleted"] = (!useComponentGraph || componentAnalysisComplete).ToString().ToLowerInvariant(),
                ["GraphLevel"] = useComponentGraph ? "ArchitecturalComponents" : "BuildModules",
                ["Language"] = context.Language,
                ["Framework"] = context.Framework ?? "Unknown"
            }
        };

        if (!useComponentGraph || componentAnalysisComplete)
            summary.Metrics["CircularDependencyCount"] = cycles.Count;
        else
            summary.Metadata["IncompleteReason"] =
                "Semantic component analysis was partial; observed cycles are reported, but a clean cycle count is withheld.";

        results.Add(summary);

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Circular dependency analysis completed. Violations: {results.Count}");



        return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            results);
    }



    private ArchitectureEvaluatorResult CreateCycleResult(
        IReadOnlyCollection<string> cycle)
    {
        var firstComponent = Context?.ProductionComponents.FirstOrDefault(component =>
            component.Id.Equals(cycle.First(), StringComparison.Ordinal));
        var target = firstComponent?.SourceFiles.FirstOrDefault() ?? cycle.First();
        var result =
            CreateResult(
                target,
                nameof(ArchitectureRuleCategory.Architecture));


        result.Metrics["HasCycle"] =
            1;

        result.Metrics["CycleLength"] =
            cycle.Count;

        result.Metrics["CircularDependencyViolation"] =
            1;

        result.Metrics["CircularDependencyCompliance"] =
            0;


        result.Metadata["Cycle"] =
            string.Join(" -> ", cycle.Select(GetCycleNodeLabel));
        result.Metadata["GraphLevel"] =
            (Context?.ProductionComponents.Count ?? 0) > 0
                ? "ArchitecturalComponents"
                : "BuildModules";

        result.Metadata["Language"] =
            Context?.Language ?? "Unknown";

        result.Metadata["Framework"] =
            Context?.Framework ?? "Unknown";


        return result;
    }



    private string GetCycleNodeLabel(string nodeId)
    {
        var component = Context?.ProductionComponents.FirstOrDefault(candidate =>
            candidate.Id.Equals(nodeId, StringComparison.Ordinal));

        return component is null
            ? nodeId
            : $"{component.BuildModuleName} · {component.QualifiedName}";
    }

    private static Dictionary<string, HashSet<string>>
        BuildDependencyGraph(
            IEnumerable<ArchitectureDependencyContext> dependencies)
    {
        var graph =
            new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);


        foreach (var dependency in dependencies)
        {
            if (!graph.TryGetValue(
                    dependency.Source,
                    out var targets))
            {
                targets =
                    new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                graph[dependency.Source] =
                    targets;
            }


            targets.Add(
                dependency.Target);
        }


        return graph;
    }



    private static Dictionary<string, HashSet<string>> BuildComponentDependencyGraph(
        IEnumerable<ArchitectureComponentDependencyContext> dependencies)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.SourceComponentId)
                || string.IsNullOrWhiteSpace(dependency.TargetComponentId))
            {
                continue;
            }

            if (!graph.TryGetValue(dependency.SourceComponentId, out var targets))
            {
                targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                graph[dependency.SourceComponentId] = targets;
            }

            targets.Add(dependency.TargetComponentId);
        }

        return graph;
    }

    private static List<List<string>>
        DetectCycles(
            Dictionary<string, HashSet<string>> graph)
    {
        var cycles =
            new List<List<string>>();


        var visited =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);


        var active =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);


        var stack =
            new Stack<string>();


        foreach (var node in graph.Keys)
        {
            Visit(
                node,
                graph,
                visited,
                active,
                stack,
                cycles);
        }


        return cycles;
    }



    private static void Visit(
        string node,
        Dictionary<string, HashSet<string>> graph,
        HashSet<string> visited,
        HashSet<string> active,
        Stack<string> stack,
        List<List<string>> cycles)
    {
        if (active.Contains(node))
        {
            var cycle =
                stack
                    .Reverse()
                    .SkipWhile(
                        x => !x.Equals(
                            node,
                            StringComparison.OrdinalIgnoreCase))
                    .Append(node)
                    .ToList();


            cycles.Add(cycle);

            return;
        }


        if (!visited.Add(node))
            return;


        active.Add(node);
        stack.Push(node);



        if (graph.TryGetValue(
                node,
                out var targets))
        {
            foreach (var target in targets)
            {
                Visit(
                    target,
                    graph,
                    visited,
                    active,
                    stack,
                    cycles);
            }
        }



        stack.Pop();
        active.Remove(node);
    }
}

