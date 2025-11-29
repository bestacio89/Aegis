using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Infrastructure;

/// <summary>
/// Evaluates logging hygiene across .NET, Java, Python, Node, and TypeScript ecosystems.
/// Produces quantitative metrics for log hygiene, structured logging compliance,
/// and observability maturity (LoggingIntegrityScore - ObservabilityHealthIndex).
/// </summary>
public sealed class LoggingEvaluator : BaseArchitectureEvaluator
{
    private readonly LoggingPolicy _policy;

    public override string Name => "LoggingEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Java", "Python", "TypeScript", "JavaScript"];
    public override string[] SupportedFrameworks => ["Serilog", "NLog", "Log4j", "Winston", "Python.Logging"];

    // Regex patterns
    private static readonly Regex PlainConsoleRx = new(@"(Console\.Write(Line)?|print|System\.out\.println|console\.log)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HardcodedSecretRx = new(@"\b(password|secret|apikey|token|connectionstring)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LogLevelRx = new(@"\b(Log(ger)?\s*\.\s*(Trace|Debug|Info|Warn|Error|Fatal)|logger\.log\()", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InterpolatedStringRx = new(@"(\$""|format\()", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LoggingEvaluator(ILogger<LoggingEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Logging ?? new LoggingPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.Enabled)
        {
            _logger.LogInformation("⏭ {Evaluator} disabled by policy.", Name);
            return results;
        }

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".java") || f.EndsWith(".py") || f.EndsWith(".ts") || f.EndsWith(".js"))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        _logger.LogInformation("🧩 Running {Evaluator} on {Count} source files", Name, files.Count);

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            string content;
            try { content = await File.ReadAllTextAsync(file, token); }
            catch { continue; }

            // Initialize base metric scores
            double structuredScore = 1.0;
            double levelScore = 1.0;
            double safetyScore = 1.0;
            double noiseScore = 1.0;

            // ⚠️ Console or print logging misuse
            if (_policy.DisallowConsoleOnlyLogging && PlainConsoleRx.IsMatch(content))
                structuredScore = 0.5;

            // ✅ Structured logging framework detection
            string language = DetectLanguage(file);
            if (_policy.RequireStructuredLogging && _policy.FrameworkHints.TryGetValue(language, out var frameworks))
            {
                if (!frameworks.Any(fw => content.Contains(fw, StringComparison.OrdinalIgnoreCase)))
                    structuredScore = 0.0;
            }

            // 🧩 Log level usage check
            if (_policy.RequireLogLevels && !LogLevelRx.IsMatch(content))
                levelScore = 0.5;

            // 🚫 Hardcoded secrets exposure
            if (_policy.DetectHardcodedSecretsInLogs && HardcodedSecretRx.IsMatch(content))
                safetyScore = 0.0;

            // 🧱 Excessive interpolated strings
            int interpolationCount = InterpolatedStringRx.Matches(content).Count;
            if (_policy.WarnInterpolatedStrings && interpolationCount > 5)
                noiseScore = Math.Max(0, 1 - interpolationCount / 10.0);

            // 🧮 Compute composite Logging Integrity Score
            double integrityScore = ComputeIntegrity(structuredScore, levelScore, safetyScore, noiseScore);

            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Infrastructure",
                Metrics = new Dictionary<string, double>
                {
                    ["StructuredScore"] = structuredScore,
                    ["LevelUsageScore"] = levelScore,
                    ["SafetyScore"] = safetyScore,
                    ["NoiseScore"] = noiseScore,
                    ["LoggingIntegrityScore"] = integrityScore
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = Path.GetFileName(file),
                    ["Language"] = language,
                    ["Frameworks"] = string.Join(", ", _policy.FrameworkHints.GetValueOrDefault(language) ?? Array.Empty<string>()),
                    ["PolicyEnabled"] = _policy.Enabled.ToString()
                }
            });
        }

        // 📊 Aggregate project-level observability summary
        if (results.Count > 0)
        {
            double avgIntegrity = results.Average(r => r.Metrics.GetValueOrDefault("LoggingIntegrityScore", 0));
            double avgStructured = results.Average(r => r.Metrics.GetValueOrDefault("StructuredScore", 0));
            double avgSafety = results.Average(r => r.Metrics.GetValueOrDefault("SafetyScore", 0));

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "InfrastructureSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["AnalyzedFiles"] = results.Count,
                    ["AverageIntegrityScore"] = avgIntegrity,
                    ["AverageStructuredScore"] = avgStructured,
                    ["AverageSafetyScore"] = avgSafety,
                    ["ObservabilityHealthIndex"] = avgIntegrity * 0.6 + avgStructured * 0.25 + avgSafety * 0.15
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

    private static double ComputeIntegrity(double structured, double level, double safety, double noise)
    {
        // Weighted model emphasizing structure & safety
        double score = structured * 0.35 + level * 0.2 + safety * 0.3 + noise * 0.15;
        return Math.Round(score * 100, 2);
    }

    private static string DetectLanguage(string path)
    {
        if (path.EndsWith(".cs")) return "CSharp";
        if (path.EndsWith(".java")) return "Java";
        if (path.EndsWith(".py")) return "Python";
        if (path.EndsWith(".ts") || path.EndsWith(".js")) return "Node";
        return "Unknown";
    }
}
