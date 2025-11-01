using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Enums;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.Architecture;

/// <summary>
/// Evaluates architectural dependency relations and layer boundary compliance.
/// Produces normalized EvaluatorResults representing cross-layer references.
/// The RuleEngine later interprets policy violations.
/// </summary>
public sealed class ArchitecturalEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly ArchitecturePolicy _policy;

    public override string Name => "ArchitecturalEvaluator";

    public override string[] SupportedLanguages => ["CSharp", "Java", "Python", "TypeScript", "JavaScript"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring Boot", "FastAPI", "Node", "Angular", "React", "Vue"];

    private static readonly Regex UsingRegex =
        new(@"^\s*(using|import)\s+([A-Za-z0-9_.\-]+)", RegexOptions.Multiline | RegexOptions.Compiled);

    public ArchitecturalEvaluator(
        ILogger<ArchitecturalEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture ?? new ArchitecturePolicy();
    }

    /// <summary>
    /// Scans all relevant source files for cross-layer references.
    /// Produces EvaluatorResults representing each dependency relation.
    /// </summary>
    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceLayerBoundaries)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                "🏗️ Architectural layer enforcement disabled by policy.");
            return results;
        }

        var extensions = Context?.Language switch
        {
            "CSharp" => new[] { ".cs" },
            "Java" => new[] { ".java" },
            "Python" => new[] { ".py" },
            "TypeScript" => new[] { ".ts" },
            "JavaScript" => new[] { ".js" },
            _ => new[] { ".cs", ".java", ".py", ".ts", ".js" }
        };

        var files = Directory
            .EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(Path.GetDirectoryName(f)!))
            .ToList();

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🔍 Scanning {files.Count} files for architectural dependencies ({Context?.Language}/{Context?.Framework}).");

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            var language = DetectLanguage(file);
            var enforcement = _policy.EnforcementByLanguage.GetValueOrDefault(language, EnforcementMode.Strict);
            if (enforcement == EnforcementMode.Ignored)
                continue;

            var fromLayer = DetectLayer(file, projectPath);
            if (fromLayer is null)
                continue;

            var content = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);

            foreach (Match match in UsingRegex.Matches(content))
            {
                var ns = match.Groups[2].Value;
                var toLayer = MapNamespaceToLayer(ns);

                // Skip same-layer or unrecognized dependencies
                if (toLayer is null || toLayer.Equals(fromLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Determine allowance
                var isAllowed = _policy.AllowedDependencies.TryGetValue(fromLayer, out var allowedTargets)
                    && allowedTargets.Contains(toLayer, StringComparer.OrdinalIgnoreCase);

                results.Add(new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "Architecture",
                    Metrics = { ["AllowedDependency"] = isAllowed ? 1 : 0 },
                    Metadata = new Dictionary<string, string>
                    {
                        ["FromLayer"] = fromLayer,
                        ["ToLayer"] = toLayer,
                        ["Reference"] = ns,
                        ["Language"] = language,
                        ["EnforcementMode"] = enforcement.ToString(),
                        ["AllowedTargets"] = string.Join(",", allowedTargets ?? Array.Empty<string>()),
                        ["Allowed"] = isAllowed.ToString()
                    }
                });
            }
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
            $"🏗️ Architectural evaluation complete with {results.Count} dependency relation(s) logged.");

        return results;
    }

    // ---------------------------------------------
    // Helpers
    // ---------------------------------------------

    private static string DetectLanguage(string file)
    {
        if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return "CSharp";
        if (file.EndsWith(".java", StringComparison.OrdinalIgnoreCase)) return "Java";
        if (file.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) return "Python";
        if (file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)) return "TypeScript";
        if (file.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) return "JavaScript";
        return "Unknown";
    }

    private string? DetectLayer(string file, string root)
    {
        var relativePath = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);

        foreach (var (layer, hints) in _policy.LayerHints)
        {
            if (hints.Any(h => relativePath.Contains(h, StringComparison.OrdinalIgnoreCase)))
                return layer;
        }

        return null;
    }

    private string? MapNamespaceToLayer(string ns)
    {
        foreach (var (layer, hints) in _policy.LayerHints)
        {
            if (hints.Any(h => ns.Contains(h, StringComparison.OrdinalIgnoreCase)))
                return layer;
        }

        return null;
    }
}
