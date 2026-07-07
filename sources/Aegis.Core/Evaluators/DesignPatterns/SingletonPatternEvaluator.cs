using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Quantitatively evaluates Singleton pattern correctness across supported languages.
/// Computes a SingletonComplianceScore (0–100) based on thread safety, initialization strategy,
/// and pattern adherence consistency.
/// </summary>
public sealed class SingletonPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "SingletonPatternEvaluator";
    public override string[] SupportedLanguages => ["C#", "Java", "Python"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring", "Flask", "FastAPI"];

    private static readonly Regex SingletonClassRx =
        new(@"class\s+(\w+)\b.*\bstatic\s+\1\s*", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex GetInstanceRx =
        new(@"getInstance\s*\(", RegexOptions.Compiled);
    private static readonly Regex ThreadLockRx =
        new(@"lock\s*\(|synchronized\s*\(", RegexOptions.Compiled);
    private static readonly Regex PythonSingletonRx =
        new(@"class\s+\w+\(Singleton\)", RegexOptions.Compiled);

    public SingletonPatternEvaluator(ILogger<SingletonPatternEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture?.DesignPatterns ?? new DesignPatternPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceSingletonPattern)
        {
            _logger.LogInformation("⏭ Singleton pattern enforcement disabled by policy.");
            return results;
        }

        _logger.LogInformation("🧠 Running {Evaluator} on project {Path}", Name, projectPath);

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => new[] { ".cs", ".java", ".py" }.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(Path.GetDirectoryName(f) ?? string.Empty))
            .ToList();

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            string content;
            try { content = await File.ReadAllTextAsync(file, token); }
            catch { continue; }

            bool isSingleton = SingletonClassRx.IsMatch(content) || GetInstanceRx.IsMatch(content) || PythonSingletonRx.IsMatch(content);
            if (!isSingleton) continue;

            var match = SingletonClassRx.Match(content);
            var className = match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(file);

            bool hasThreadSafety = ThreadLockRx.IsMatch(content);
            bool isLazy = content.Contains("Lazy<", StringComparison.OrdinalIgnoreCase)
                       || content.Contains("lazy", StringComparison.OrdinalIgnoreCase)
                       || content.Contains("synchronized", StringComparison.OrdinalIgnoreCase);
            bool multipleInstances = SingletonClassRx.Matches(content).Count > 1;
            bool correctNaming = file.EndsWith(_policy.SingletonSuffix + Path.GetExtension(file), StringComparison.OrdinalIgnoreCase);

            // 🧮 Derived Metrics
            double threadSafetyScore = hasThreadSafety ? 1.0 : 0.0;
            double lazyInitScore = isLazy ? 1.0 : 0.0;
            double instanceDiscipline = multipleInstances ? 0.0 : 1.0;
            double namingAdherence = correctNaming ? 1.0 : 0.0;

            // 🔢 Compliance scoring
            double complianceScore = ComputeCompliance(threadSafetyScore, lazyInitScore, instanceDiscipline, namingAdherence);

            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "DesignPattern",
                Metrics = new Dictionary<string, double>
                {
                    ["IsSingleton"] = 1,
                    ["ThreadSafetyScore"] = threadSafetyScore,
                    ["LazyInitializationScore"] = lazyInitScore,
                    ["InstanceDisciplineScore"] = instanceDiscipline,
                    ["NamingAdherenceScore"] = namingAdherence,
                    ["SingletonComplianceScore"] = complianceScore
                },
                Metadata = new Dictionary<string, string>
                {
                    ["ClassName"] = className,
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown",
                    ["ThreadSafetyDetected"] = hasThreadSafety.ToString(),
                    ["LazyInitializationDetected"] = isLazy.ToString(),
                    ["MultipleInstancesFound"] = multipleInstances.ToString(),
                    ["Policy_SingletonSuffix"] = _policy.SingletonSuffix
                }
            });
        }

        // 📈 Aggregate health
        if (results.Count > 0)
        {
            double avgScore = results.Average(r => r.Metrics.GetValueOrDefault("SingletonComplianceScore", 0));
            double avgThreadSafe = results.Average(r => r.Metrics.GetValueOrDefault("ThreadSafetyScore", 0));
            double avgLazyInit = results.Average(r => r.Metrics.GetValueOrDefault("LazyInitializationScore", 0));
            double violationCount = results.Count(r => r.Metrics.GetValueOrDefault("InstanceDisciplineScore", 0) == 0);

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "DesignPatternSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["SingletonCount"] = results.Count,
                    ["AverageComplianceScore"] = avgScore,
                    ["AverageThreadSafety"] = avgThreadSafe,
                    ["AverageLazyInitialization"] = avgLazyInit,
                    ["ViolationCount"] = violationCount,
                    ["OverallSingletonHealth"] = avgScore * (1 - violationCount / Math.Max(1.0, results.Count))
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.EnforceSingletonPattern.ToString()
                }
            });
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} metric entries", Name, results.Count);
        return results;
    }

    private static double ComputeCompliance(double threadSafety, double lazyInit, double instanceDiscipline, double namingAdherence)
    {
        // Weighted scoring emphasizing safety and discipline
        double score =
            threadSafety * 0.35 +
            lazyInit * 0.25 +
            instanceDiscipline * 0.3 +
            namingAdherence * 0.1;

        return Math.Round(score * 100, 2);
    }
}
