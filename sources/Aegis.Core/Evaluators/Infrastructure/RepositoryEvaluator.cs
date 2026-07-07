using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Infrastructure;

/// <summary>
/// Evaluates repository hygiene, documentation, and structural conformance
/// according to <see cref="RepositoryPolicy"/> and quantifies overall repository governance quality.
/// Produces RepositoryHealthIndex and supporting metrics.
/// </summary>
public sealed class RepositoryEvaluator : BaseArchitectureEvaluator
{
    private readonly RepositoryPolicy _policy;

    public override string Name => "RepositoryEvaluator";
    public override string[] SupportedLanguages => ["C#", "JavaScript", "TypeScript", "Python"];
    public override string[] SupportedFrameworks => ["DotNet", "Node", "Python", "Platform"];

    public RepositoryEvaluator(ILogger<RepositoryEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Repository ?? new RepositoryPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();
        _logger.LogInformation("🏗️ Evaluating repository hygiene at {Path}", projectPath);

        // Initialize base quantitative metrics
        double governanceScore = 1.0;
        double ciScore = 1.0;
        double structureScore = 1.0;
        double dependencyScore = 1.0;
        double maintainabilityScore = 1.0;

        // ============================================================
        // (1) Governance Files
        // ============================================================
        if (_policy.EnforceGovernanceFiles)
        {
            foreach (var required in _policy.RequiredFiles)
            {
                var path = Path.Combine(projectPath, required);
                if (!File.Exists(path))
                    governanceScore -= 0.1;
            }

            if (_policy.RequireContributingGuide && !File.Exists(Path.Combine(projectPath, "CONTRIBUTING.md")))
                governanceScore -= 0.05;

            if (_policy.RequireVersionFile &&
                !File.Exists(Path.Combine(projectPath, "CHANGELOG.md")) &&
                !File.Exists(Path.Combine(projectPath, "version.json")))
                governanceScore -= 0.05;
        }

        // ============================================================
        // (2) CI/CD Presence
        // ============================================================
        if (_policy.EnforceCiPresence)
        {
            bool hasCi = _policy.CiPaths.Any(p =>
                Directory.Exists(Path.Combine(projectPath, p)) ||
                File.Exists(Path.Combine(projectPath, p)));

            if (!hasCi)
                ciScore -= 0.4;
        }

        // ============================================================
        // (3) Large Files & Structural Hygiene
        // ============================================================
        if (_policy.CheckLargeFiles)
        {
            var largeFiles = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
                .Where(f => new FileInfo(f).Length > _policy.MaxFileSizeBytes)
                .Count();

            if (largeFiles > 0)
                structureScore -= Math.Min(0.2, largeFiles * 0.02);
        }

        // ============================================================
        // (4) TODO/FIXME Density
        // ============================================================
        if (_policy.CheckTodoDensity)
        {
            int totalTodos = 0, totalFiles = 0;
            foreach (var f in Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".ts") || f.EndsWith(".js") || f.EndsWith(".py")))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var content = await File.ReadAllTextAsync(f, token);
                    totalTodos += Regex.Matches(content, @"\b(TODO|FIXME)\b", RegexOptions.IgnoreCase).Count;
                    totalFiles++;
                }
                catch { }
            }

            if (totalFiles > 0 && totalTodos / (double)totalFiles >= _policy.MaxTodoDensity)
                maintainabilityScore -= 0.15;
        }

        // ============================================================
        // (5) Nested Depth & File Count
        // ============================================================
        var depth = Directory.GetDirectories(projectPath, "*", SearchOption.AllDirectories)
            .Select(d => d.Replace(projectPath, "").Count(c => c == Path.DirectorySeparatorChar))
            .DefaultIfEmpty(0)
            .Max();
        if (depth > _policy.MaxNestedDepth)
            structureScore -= 0.1;

        var totalFileCount = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories).Count();
        if (totalFileCount > _policy.MaxFileCount)
            structureScore -= 0.1;

        // ============================================================
        // (6) Dependency Lock & License Consistency
        // ============================================================
        if (_policy.RequireDependencyLock)
        {
            bool hasLock = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
                .Any(f => f.EndsWith("package-lock.json") ||
                          f.EndsWith("poetry.lock") ||
                          f.EndsWith("Pipfile.lock") ||
                          f.EndsWith("yarn.lock") ||
                          f.EndsWith("packages.lock.json"));
            if (!hasLock)
                dependencyScore -= 0.2;
        }

        if (_policy.RequireLicenseConsistency)
        {
            var licenses = Directory.EnumerateFiles(projectPath, "LICENSE*", SearchOption.TopDirectoryOnly);
            if (licenses.Count() > 1)
                governanceScore -= 0.05;
        }

        // ============================================================
        // (7) Project Density
        // ============================================================
        var projects = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".csproj") || f.EndsWith("package.json") || f.EndsWith("pom.xml"))
            .Count();

        if (projects > _policy.MaxProjectsPerRepo)
            structureScore -= 0.15;

        // ============================================================
        // 🧮 Compute Overall Repository Health Index
        // ============================================================
        double repoHealth = ComputeRepositoryHealth(governanceScore, ciScore, structureScore, dependencyScore, maintainabilityScore);

        results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
        {
            Category = "Repository",
            Metrics = new Dictionary<string, double>
            {
                ["GovernanceScore"] = Math.Max(0, governanceScore),
                ["CICDScore"] = Math.Max(0, ciScore),
                ["StructureScore"] = Math.Max(0, structureScore),
                ["DependencyScore"] = Math.Max(0, dependencyScore),
                ["MaintainabilityScore"] = Math.Max(0, maintainabilityScore),
                ["RepositoryHealthIndex"] = repoHealth
            },
            Metadata = new Dictionary<string, string>
            {
                ["Evaluator"] = Name,
                ["PolicyEnabled"] = _policy.EnforceGovernanceFiles.ToString(),
                ["ProjectPath"] = projectPath
            }
        });

        _logger.LogInformation("✅ {Evaluator} completed with RepositoryHealthIndex={Health:F2}", Name, repoHealth);
        return results;
    }

    private static double ComputeRepositoryHealth(double gov, double ci, double structure, double dep, double maintain)
    {
        double score = gov * 0.25 + ci * 0.2 + structure * 0.25 + dep * 0.15 + maintain * 0.15;
        return Math.Round(score * 100, 2);
    }
}
