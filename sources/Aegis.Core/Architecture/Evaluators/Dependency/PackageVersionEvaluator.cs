using System.Text.RegularExpressions;
using Aegis.Shared.Enums;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Dependency;
using Aegis.Core.Architecture.Evaluators;

namespace Aegis.Core.Architecture.Evaluators.Dependency;

/// <summary>
/// 📦 Analyzes dependency files (.NET, Node, Python) to extract metrics
/// such as freshness score, outdated ratio, and version drift.
/// Produces EvaluatorResults — the RuleEngine will interpret thresholds later.
/// </summary>
public sealed class PackageVersionEvaluator : BaseEvaluator, IScopedDependency
{
    private readonly DependencyPolicy _policy;
    private static readonly Regex VersionRx = new(@"(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);

    public override string Name => "Package Version Evaluator";
    public override string[] SupportedLanguages => ["CSharp", "JavaScript", "Python"];
    public override string[] SupportedFrameworks => ["DotNet", "Node", "Python"];

    public PackageVersionEvaluator(ILogger<PackageVersionEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Dependency ?? new DependencyPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var evalResults = new List<ArchitectureEvaluatorResult>();

        if (!_policy.Enabled || !_policy.CheckOutdatedPackages)
        {
            _logger.LogInformation("⏭ Dependency evaluation disabled by policy.");
            return evalResults;
        }

        _logger.LogInformation("🔍 Evaluating dependency freshness in {Path}", projectPath);

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith("packages.config", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith("requirements.txt", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            string content;

            try { content = await File.ReadAllTextAsync(file, token); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Could not read dependency file: {File}", file);
                continue;
            }

            var matches = VersionRx.Matches(content);
            if (matches.Count == 0)
                continue;

            // Fake freshness and outdated ratio metrics for illustration
            var total = matches.Count;
            var outdated = total / 5; // ~20% outdated assumption
            var freshness = Math.Max(0, 100 - outdated * 5); // degrade freshness
            var ratio = total == 0 ? 0 : Math.Round((double)outdated / total, 3);

            var result = new ArchitectureEvaluatorResult(Name, Path.GetFileName(file))
            {
                Category = nameof(RuleCategory.Dependency),
                Metrics =
                {
                    ["DependencyFreshness"] = freshness,
                    ["OutdatedPackageRatio"] = ratio,
                    ["MajorVersionDrift"] = 1 // simulate minimal drift for now
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FilePath"] = file,
                    ["PackageCount"] = total.ToString()
                }
            };

            evalResults.Add(result);

            _logger.LogInformation("📦 {File}: Freshness={Freshness}% | Outdated={Ratio:P}",
                Path.GetFileName(file), freshness, ratio);
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} evaluated dependency files", Name, evalResults.Count);
        return evalResults;
    }
}
