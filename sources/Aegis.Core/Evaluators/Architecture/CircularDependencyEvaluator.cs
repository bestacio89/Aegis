using Aegis.Architecture.Diagnostics;
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


        if (Context is null ||
            Context.Dependencies.Count == 0)
        {
            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
                results);
        }



        var graph =
            BuildDependencyGraph(
                Context.Dependencies);



        var cycles =
            DetectCycles(graph);



        foreach (var cycle in cycles)
        {
            token.ThrowIfCancellationRequested();


            results.Add(
                CreateViolation(
                    cycle));
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Circular dependency analysis completed. Violations: {results.Count}");


        return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            results);
    }



    private ArchitectureEvaluatorResult CreateViolation(
        IReadOnlyCollection<string> cycle)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            cycle.First())
        {
            Category = "CircularDependency",

            Metrics =
            {
                ["HasCycle"] = 1,
                ["CycleLength"] = cycle.Count
            },

            Metadata =
            {
                ["Cycle"] =
                    string.Join(
                        " -> ",
                        cycle),

                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown"
            }
        };
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



        if (graph.TryGetValue(node, out var targets))
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