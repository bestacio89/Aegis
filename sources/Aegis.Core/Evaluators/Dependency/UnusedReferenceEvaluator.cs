using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Aegis.Shared.Models.Policies;
using Aegis.Shared.Models.Policies.Dependency;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Core.Evaluators.Dependency;

/// <summary>
/// 🧹 Evaluates dependency manifests (.csproj, package.json, requirements.txt)
/// for unused or redundant references.
/// Produces EvaluatorResults containing metrics:
/// - UnusedReferenceCount
/// - TotalReferenceCount
/// - UnusedReferenceRatio
/// The RuleEngine will later interpret these metrics using DependencyRuleset.
/// </summary>
public sealed class UnusedReferenceEvaluator : BaseEvaluator, IScopedDependency
{
    private readonly DependencyPolicy _policy;

    public override string Name => "Unused Reference Evaluator";
    public override string[] SupportedLanguages => ["CSharp", "TypeScript", "Python"];
    public override string[] SupportedFrameworks => ["DotNet", "Node", "Python"];

    public UnusedReferenceEvaluator(ILogger<UnusedReferenceEvaluator> logger, IOptions<AegisPolicy> options)
        : base(logger)
    {
        _policy = options.Value.Dependency ?? new DependencyPolicy();
    }

    /// <summary>
    /// Performs dependency hygiene analysis and returns structured metrics.
    /// </summary>
    protected override async Task<IEnumerable<EvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var evalResults = new List<EvaluatorResult>();

        if (!_policy.Enabled || !_policy.CheckUnusedReferences)
        {
            _logger.LogInformation("⏭ {Evaluator} disabled by policy.", Name);
            return evalResults;
        }

        _logger.LogInformation("🧹 Running {Evaluator} under {Path}", Name, projectPath);

        // Detect manifests across ecosystems
        var manifestFiles = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f =>
                f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("package.json", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("requirements.txt", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        foreach (var file in manifestFiles)
        {
            token.ThrowIfCancellationRequested();

            string content;
            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Could not read dependency file: {File}", file);
                continue;
            }

            // Basic heuristic — lines containing “Unused”, “Obsolete”, or commented packages
            var lines = content.Split('\n');
            var totalRefs = lines.Length;
            var unusedCount = lines.Count(l =>
                l.Contains("Unused", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Obsolete", StringComparison.OrdinalIgnoreCase) ||
                l.TrimStart().StartsWith("#", StringComparison.OrdinalIgnoreCase) && l.Contains("package", StringComparison.OrdinalIgnoreCase));

            // Compute hygiene metrics
            var ratio = totalRefs == 0 ? 0 : Math.Round((double)unusedCount / totalRefs, 3);

            var result = new EvaluatorResult(Name, Path.GetFileName(file))
            {
                Category = nameof(RuleCategory.Dependency),
                Metrics =
                {
                    ["UnusedReferenceCount"] = unusedCount,
                    ["TotalReferenceCount"] = totalRefs,
                    ["UnusedReferenceRatio"] = ratio
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FilePath"] = file,
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown"
                }
            };

            evalResults.Add(result);

            _logger.LogInformation(
                "📦 {File}: {Unused}/{Total} unused refs ({Ratio:P})",
                Path.GetFileName(file),
                unusedCount,
                totalRefs,
                ratio
            );
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} evaluated files", Name, evalResults.Count);
        return evalResults;
    }
}
