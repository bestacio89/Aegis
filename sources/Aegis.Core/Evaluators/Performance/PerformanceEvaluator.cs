using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Performance;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Performance;

/// <summary>
/// Evaluates performance, concurrency, and async best practices across .NET, Java, Python, and Node ecosystems.
/// Produces PerformanceHealthIndex and sub-metrics for algorithmic efficiency and async maturity.
/// </summary>
public sealed class PerformanceEvaluator : BaseArchitectureEvaluator
{
    private readonly PerformancePolicy _policy;

    public override string Name => "PerformanceEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Java", "Python", "TypeScript", "JavaScript"];
    public override string[] SupportedFrameworks => ["DotNet", "Spring", "Node", "FastAPI", "Flask"];

    private static readonly Regex LoopRx = new(@"\b(for|while|foreach)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NestedLoopRx = new(@"\b(for|while)[^{]*\{[^}]*\b(for|while)\b", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StringConcatRx = new(@"\+\s*=", RegexOptions.Compiled);
    private static readonly Regex BlockingRx = new(@"\b(Thread\.Sleep|Task\.Wait|\.Result|time\.sleep|await\s+Task\.Delay\(0\))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ThreadSpawnRx = new(@"\bnew\s+(Thread|Task)\b|\bThreadPool\.QueueUserWorkItem\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LargeCollectionRx = new(@"\bnew\s+(List|Array|HashMap|Dictionary|Set)\s*<.*>\s*\([^)]*(1000|10_000|10000)[^)]*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SyncIOCallRx = new(@"\b(File\.ReadAllText|ReadToEnd|open\(|fs\.readFileSync|java\.nio\.file\.Files\.readAll)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public PerformanceEvaluator(ILogger<PerformanceEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Performance ?? new PerformancePolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.Enabled)
        {
            _logger.LogInformation("⏭ {Evaluator} disabled by policy.", Name);
            return results;
        }

        _logger.LogInformation("🚀 Running {Evaluator} on {Path}", Name, projectPath);

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".java") || f.EndsWith(".py") || f.EndsWith(".ts") || f.EndsWith(".js"))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;
            try { content = await File.ReadAllTextAsync(file, token); }
            catch { continue; }

            // Initialize performance sub-scores
            double concurrencyScore = 1.0;
            double asyncScore = 1.0;
            double algorithmicScore = 1.0;
            double ioScore = 1.0;

            // 🔁 Nested loop detection
            var loopCount = LoopRx.Matches(content).Count;
            if (loopCount > 0 && NestedLoopRx.IsMatch(content))
                algorithmicScore -= 0.2;

            // 📏 Loop body size penalty
            var loopBodies = Regex.Matches(content, @"(for|while|foreach)\s*\(.*\)\s*\{([\s\S]*?)\}", RegexOptions.Compiled);
            foreach (Match loop in loopBodies)
            {
                int lineCount = loop.Groups[2].Value.Split('\n').Length;
                if (lineCount > _policy.MaxLoopBodyLength)
                    algorithmicScore -= 0.05;
            }

            // 🧮 String concatenation inside loops
            if (_policy.DetectStringConcatenationInLoops && StringConcatRx.IsMatch(content) && content.Contains("for"))
                algorithmicScore -= 0.05;

            // ⛔ Blocking calls
            if (_policy.DisallowBlockingCalls && BlockingRx.IsMatch(content))
                concurrencyScore -= 0.25;

            // 🧵 Thread spawning
            if (_policy.WarnThreadCreationInHotPaths && ThreadSpawnRx.IsMatch(content))
                concurrencyScore -= 0.15;

            // 🧱 Large collection init
            if (_policy.DetectLargeCollectionsInitialization && LargeCollectionRx.IsMatch(content))
                algorithmicScore -= 0.05;

            // 🧩 Sync I/O calls
            if (_policy.RequireAsyncForIOMethods && SyncIOCallRx.IsMatch(content))
                ioScore -= 0.2;

            // 💤 Thread.Sleep usage
            if (_policy.CheckThreadSleepUsage && content.Contains("Thread.Sleep", StringComparison.OrdinalIgnoreCase))
                concurrencyScore -= 0.1;

            // 🪶 Suggest async streams
            if (_policy.SuggestAsyncStreams && content.Contains("foreach") && !content.Contains("await"))
                asyncScore -= 0.1;

            // Compute overall performance health
            double performanceHealth = ComputePerformanceHealth(algorithmicScore, concurrencyScore, asyncScore, ioScore);

            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Performance",
                Metrics = new Dictionary<string, double>
                {
                    ["AlgorithmicEfficiencyScore"] = Math.Max(0, algorithmicScore),
                    ["ConcurrencyScore"] = Math.Max(0, concurrencyScore),
                    ["AsyncUsageScore"] = Math.Max(0, asyncScore),
                    ["IOMaturityScore"] = Math.Max(0, ioScore),
                    ["PerformanceHealthIndex"] = performanceHealth
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = Path.GetFileName(file),
                    ["Language"] = DetectLanguage(file),
                    ["PolicyEnabled"] = _policy.Enabled.ToString()
                }
            });
        }

        // 📊 Aggregate summary
        if (results.Count > 0)
        {
            double avgHealth = results.Average(r => r.Metrics.GetValueOrDefault("PerformanceHealthIndex", 0));
            double avgConcurrency = results.Average(r => r.Metrics.GetValueOrDefault("ConcurrencyScore", 0));
            double avgAlgorithmic = results.Average(r => r.Metrics.GetValueOrDefault("AlgorithmicEfficiencyScore", 0));

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "PerformanceSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["AnalyzedFiles"] = results.Count,
                    ["AveragePerformanceHealthIndex"] = avgHealth,
                    ["AverageConcurrencyScore"] = avgConcurrency,
                    ["AverageAlgorithmicScore"] = avgAlgorithmic,
                    ["EfficiencyHealthIndex"] = avgHealth * 0.7 + avgConcurrency * 0.2 + avgAlgorithmic * 0.1
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.Enabled.ToString(),
                    ["SupportedFrameworks"] = string.Join(", ", SupportedFrameworks)
                }
            });
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} results", Name, results.Count);
        return results;
    }

    private static double ComputePerformanceHealth(double algo, double concurrency, double async, double io)
    {
        double score = algo * 0.35 + concurrency * 0.3 + async * 0.2 + io * 0.15;
        return Math.Round(score * 100, 2);
    }

    private static string DetectLanguage(string path)
    {
        if (path.EndsWith(".cs")) return "CSharp";
        if (path.EndsWith(".java")) return "Java";
        if (path.EndsWith(".py")) return "Python";
        if (path.EndsWith(".ts")) return "TypeScript";
        if (path.EndsWith(".js")) return "JavaScript";
        return "Unknown";
    }
}
