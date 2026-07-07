using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Quantitatively evaluates repository pattern adherence and data isolation discipline.
/// Detects and scores: interface abstraction, async compliance, persistence leaks,
/// and excessive repository complexity ("God Repository").
/// Produces RepositoryComplianceScore (0–100).
/// </summary>
public sealed class RepositoryPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "RepositoryPatternEvaluator";
    public override string[] SupportedLanguages => ["C#", "Java", "Python"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring", "Django", "FastAPI", "Flask"];

    private static readonly Regex RepoClassRx = new(@"class\s+(\w+Repository)\b", RegexOptions.Compiled);
    private static readonly Regex InterfaceRx = new(@"interface\s+I(\w+Repository)\b", RegexOptions.Compiled);
    private static readonly Regex MethodRx = new(@"\b(public|protected|def)\s+\w+\s*\(", RegexOptions.Compiled);
    private static readonly Regex FieldRx = new(@"\b(private|protected|var|self\.)\s*\w+", RegexOptions.Compiled);
    private static readonly Regex AsyncRx = new(@"async\s+|\bCompletableFuture<", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] LeakIndicators =
        ["SqlConnection", "DbContext", "EntityManager", "cursor.execute", "HttpClient", "axios", "fetch(", "open("];

    public RepositoryPatternEvaluator(ILogger<RepositoryPatternEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns ?? new();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceRepositoryPattern)
        {
            _logger.LogInformation("🧩 Repository pattern enforcement disabled by policy.");
            return results;
        }

        var extensions = Context?.Language switch
        {
            "C#" => new[] { ".cs" },
            "Java" => new[] { ".java" },
            "Python" => new[] { ".py" },
            _ => new[] { ".cs", ".java", ".py" }
        };

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        _logger.LogInformation("📦 Running {Evaluator} on {Count} files", Name, files.Count);

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            string content;
            try { content = await File.ReadAllTextAsync(file, token); }
            catch { continue; }

            foreach (Match match in RepoClassRx.Matches(content))
            {
                var repoName = match.Groups[1].Value;
                var fileName = Path.GetFileName(file);

                // --- Detection booleans ---
                bool hasInterface = InterfaceRx.IsMatch(content);
                bool hasAsync = AsyncRx.IsMatch(content);
                bool hasLeak = LeakIndicators.Any(k => content.Contains(k, StringComparison.OrdinalIgnoreCase));
                bool correctNaming = repoName.EndsWith(_policy.RepositorySuffix);
                int methodCount = MethodRx.Matches(content).Count;
                int fieldCount = FieldRx.Matches(content).Count;

                // --- Derived metrics ---
                double interfaceAdherence = hasInterface ? 1.0 : 0.0;
                double asyncCompliance = hasAsync ? 1.0 : 0.0;
                double leakRisk = hasLeak ? 1.0 : 0.0;
                double namingCompliance = correctNaming ? 1.0 : 0.0;
                double complexityScore = ComputeComplexity(methodCount, fieldCount);

                // --- Compliance score (weighted model) ---
                double complianceScore = ComputeCompliance(interfaceAdherence, asyncCompliance, leakRisk, namingCompliance, complexityScore);

                results.Add(new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "DesignPattern",
                    Metrics = new Dictionary<string, double>
                    {
                        ["InterfaceAdherence"] = interfaceAdherence,
                        ["AsyncCompliance"] = asyncCompliance,
                        ["LeakRisk"] = leakRisk,
                        ["NamingCompliance"] = namingCompliance,
                        ["RepositoryComplexity"] = complexityScore,
                        ["RepositoryComplianceScore"] = complianceScore
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["RepositoryName"] = repoName,
                        ["Language"] = Context?.Language ?? "Unknown",
                        ["Framework"] = Context?.Framework ?? "Unknown",
                        ["MethodCount"] = methodCount.ToString(),
                        ["FieldCount"] = fieldCount.ToString(),
                        ["HasInterface"] = hasInterface.ToString(),
                        ["HasPersistenceLeak"] = hasLeak.ToString(),
                        ["HasAsyncMethods"] = hasAsync.ToString()
                    }
                });
            }
        }

        // 🔢 Aggregated Summary
        if (results.Count > 0)
        {
            double avgCompliance = results.Average(r => r.Metrics.GetValueOrDefault("RepositoryComplianceScore", 0));
            double avgComplexity = results.Average(r => r.Metrics.GetValueOrDefault("RepositoryComplexity", 0));
            double leakCount = results.Count(r => r.Metrics.GetValueOrDefault("LeakRisk", 0) == 1);
            double asyncAdherence = results.Average(r => r.Metrics.GetValueOrDefault("AsyncCompliance", 0));

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "DesignPatternSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["RepositoryCount"] = results.Count,
                    ["AverageComplianceScore"] = avgCompliance,
                    ["AverageComplexity"] = avgComplexity,
                    ["AverageAsyncCompliance"] = asyncAdherence,
                    ["LeakCount"] = leakCount,
                    ["OverallRepositoryHealth"] = avgCompliance * (1 - leakCount / Math.Max(1, results.Count))
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.EnforceRepositoryPattern.ToString()
                }
            });
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} metric entries", Name, results.Count);
        return results;
    }

    private static double ComputeComplexity(int methodCount, int fieldCount)
    {
        double normalized = Math.Min(1.0, methodCount / 30.0 + fieldCount / 50.0);
        return Math.Round(normalized, 2);
    }

    private static double ComputeCompliance(double interfaceAdherence, double asyncCompliance, double leakRisk, double namingCompliance, double complexity)
    {
        // Weighted model emphasizing leak avoidance and abstraction
        double score =
            interfaceAdherence * 0.25 +
            asyncCompliance * 0.15 +
            (1 - leakRisk) * 0.35 +
            namingCompliance * 0.10 +
            (1 - complexity) * 0.15;

        return Math.Round(score * 100, 2);
    }
}
