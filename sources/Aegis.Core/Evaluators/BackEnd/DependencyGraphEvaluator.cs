using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.BackEnd;

/// <summary>
/// Builds a lightweight dependency graph from import/using statements across modules.
/// Emits EvaluatorResults with per-module dependency metrics for analysis and visualization.
/// </summary>
public sealed class DependencyGraphEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DependencyGraphPolicy _policy;

    public override string Name => "DependencyGraphEvaluator";

    public override string[] SupportedLanguages => ["CSharp", "Java", "TypeScript", "JavaScript"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring Boot", "Angular", "React", "Node"];

    public DependencyGraphEvaluator(
        ILogger<DependencyGraphEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.DependencyGraph ?? new DependencyGraphPolicy();
    }

    /// <summary>
    /// Collects dependency graph metrics for each module (imports/using relations).
    /// Emits one EvaluatorResult per module.
    /// </summary>
    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // 🔍 Filter relevant extensions
        var extensions = Context?.Language switch
        {
            "CSharp" => new[] { ".cs" },
            "Java" => new[] { ".java" },
            "TypeScript" => new[] { ".ts" },
            "JavaScript" => new[] { ".js" },
            _ => new[] { ".cs", ".ts", ".js", ".java" }
        };

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        if (files.Count == 0)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                $"No {Context?.Language ?? "source"} files found for dependency graph analysis.");
            return results;
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🕸️ Building dependency graph across {files.Count} {Context?.Language} file(s).");

        var importPattern = new Regex(@"(?:using|import)\s+([A-Za-z0-9_.\/]+)", RegexOptions.Compiled);

        // 🧩 Build dependency graph
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
            var moduleName = Path.GetFileNameWithoutExtension(file);

            foreach (Match match in importPattern.Matches(content))
            {
                var target = match.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(target))
                    continue;

                if (!edges.TryGetValue(moduleName, out var deps))
                {
                    deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    edges[moduleName] = deps;
                }

                deps.Add(target);
            }
        }

        // 🧮 Record metrics for each module
        foreach (var (module, dependencies) in edges)
        {
            var dependencyCount = dependencies.Count;

            results.Add(new ArchitectureEvaluatorResult(Name, module)
            {
                Category = "DependencyGraph",
                Metrics = new Dictionary<string, double>
                {
                    ["DependencyCount"] = dependencyCount,
                    ["MaxDependenciesThreshold"] = _policy.MaxDependenciesPerModule
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Module"] = module,
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown",
                    ["DependenciesList"] = string.Join(", ", dependencies.Take(10)) +
                                           (dependencies.Count > 10 ? "..." : string.Empty)
                }
            });
        }

        // Optional aggregate metric
        results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
        {
            Category = "DependencyGraphSummary",
            Metrics = new Dictionary<string, double>
            {
                ["ModuleCount"] = edges.Count,
                ["AverageDependencies"] = edges.Count == 0 ? 0 : edges.Average(e => e.Value.Count),
                ["MaxDependenciesThreshold"] = _policy.MaxDependenciesPerModule
            },
            Metadata = new Dictionary<string, string>
            {
                ["Language"] = Context?.Language ?? "Unknown",
                ["Framework"] = Context?.Framework ?? "Unknown"
            }
        });

        AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
            $"🕸️ Dependency graph built with {edges.Count} modules. Metrics collected: {results.Count} entries.");

        return results;
    }
}
