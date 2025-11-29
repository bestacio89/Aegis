using Aegis.Architecture.Diagnostics;

using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.Architecture;

/// <summary>
/// Detects circular dependencies among modules, namespaces, or import graphs.
/// Works across languages (.cs, .ts, .js, .java) and outputs structured EvaluatorResults
/// for the RuleEngine to interpret.
/// </summary>
public sealed class CircularDependencyEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    public override string Name => "CircularDependencyEvaluator";

    public override string[] SupportedLanguages => ["CSharp", "TypeScript", "JavaScript", "Java"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring Boot", "Angular", "React", "Node"];

    private readonly CircularDependencyPolicy _policy;

    private static readonly Regex ImportPattern =
        new(@"(?:using|import)\s+([A-Za-z0-9_.\/]+)", RegexOptions.Compiled);

    public CircularDependencyEvaluator(
        ILogger<CircularDependencyEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.CircularDependency ?? new CircularDependencyPolicy();
    }

    /// <summary>
    /// Core evaluation logic — builds dependency graphs and emits EvaluatorResults
    /// representing dependency relations and circular dependency detection.
    /// </summary>
    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.Enabled)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info, "⏭ Circular dependency evaluation disabled by policy.");
            return results;
        }

        // 🧩 Determine file extensions based on language context
        var extensions = Context?.Language switch
        {
            "CSharp" => new[] { ".cs" },
            "TypeScript" => new[] { ".ts" },
            "JavaScript" => new[] { ".js" },
            "Java" => new[] { ".java" },
            _ => new[] { ".cs", ".ts", ".js", ".java" }
        };

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🔍 Building dependency graph for {files.Count} files ({Context?.Language}/{Context?.Framework}).");

        // Build dependency adjacency map
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file, token);
            var source = Path.GetFileNameWithoutExtension(file);

            foreach (Match m in ImportPattern.Matches(content))
            {
                var target = m.Groups[1].Value;
                if (!graph.TryGetValue(source, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    graph[source] = set;
                }

                set.Add(target);
            }
        }

        // Detect circular dependencies
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycles = new List<List<string>>();

        foreach (var node in graph.Keys)
            DetectCycle(node, graph, visited, stack, cycles);

        foreach (var cycle in cycles)
        {
            var first = cycle.FirstOrDefault() ?? "Unknown";
            var filePath = FindPossibleFilePath(first, projectPath);
            results.Add(new ArchitectureEvaluatorResult(Name, filePath)
            {
                Category = "CircularDependency",
                Metrics = { ["HasCycle"] = 1 },
                Metadata = new Dictionary<string, string>
                {
                    ["CycleNodes"] = string.Join(" -> ", cycle),
                    ["CycleCount"] = cycle.Count.ToString(),
                    ["TreatAsError"] = _policy.TreatAsError.ToString()
                }
            });

            AegisDiagnostics.Report(Name, DiagnosticLevel.Warning,
                $"Detected circular dependency chain: {string.Join(" -> ", cycle)}");
        }

        if (results.Count == 0)
        {
            // Emit a metric to indicate clean dependency graph
            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "CircularDependency",
                Metrics = { ["HasCycle"] = 0 },
                Metadata = new Dictionary<string, string> { ["Message"] = "No circular dependencies detected." }
            });
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
            $"🔄 Circular dependency evaluation complete with {results.Count} metric(s).");

        return results;
    }

    // --------------------------------------------------
    // Helpers
    // --------------------------------------------------

    private void DetectCycle(
        string node,
        Dictionary<string, HashSet<string>> graph,
        HashSet<string> visited,
        HashSet<string> stack,
        List<List<string>> cycles)
    {
        if (stack.Contains(node))
        {
            // Detected a cycle
            var cycle = stack.SkipWhile(n => !n.Equals(node, StringComparison.OrdinalIgnoreCase)).ToList();
            cycle.Add(node);
            cycles.Add(cycle);
            return;
        }

        if (visited.Contains(node))
            return;

        visited.Add(node);
        stack.Add(node);

        if (graph.TryGetValue(node, out var deps))
        {
            foreach (var dep in deps)
                DetectCycle(dep, graph, visited, stack, cycles);
        }

        stack.Remove(node);
    }

    private static string FindPossibleFilePath(string node, string rootPath)
    {
        try
        {
            var matches = Directory.GetFiles(rootPath, $"{node}.*", SearchOption.AllDirectories);
            return matches.FirstOrDefault() ?? Path.Combine(rootPath, $"{node}.cs");
        }
        catch
        {
            return Path.Combine(rootPath, $"{node}.cs");
        }
    }
}
