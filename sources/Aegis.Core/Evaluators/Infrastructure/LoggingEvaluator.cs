using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Infrastructure;

/// <summary>
/// Evaluates logging discipline and observability maturity across
/// .NET, Java, Python, Node.js, and TypeScript ecosystems.
///
/// Produces:
/// - StructuredLoggingScore
/// - LogLevelComplianceScore
/// - SensitiveDataProtectionScore
/// - LoggingNoiseScore
/// - LoggingIntegrityScore
///
/// Aggregates into ObservabilityHealthIndex.
/// </summary>
public sealed class LoggingEvaluator : BaseArchitectureEvaluator
{
    private readonly LoggingPolicy _policy;


    public override string Name => "LoggingEvaluator";


    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "TypeScript",
        "JavaScript"
    ];


    public override string[] SupportedFrameworks =>
    [
        "Serilog",
        "NLog",
        "Log4j",
        "Winston",
        "Python.Logging"
    ];



    private static readonly Regex ConsoleLoggingRx =
        new(
            @"(Console\.Write(Line)?|print|System\.out\.println|console\.log)\s*\(",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    private static readonly Regex SensitiveLoggingRx =
        new(
            @"(?i)(password|secret|apikey|api_key|token|connectionstring|privatekey)\s*[:=]\s*['""]?[^'""]+",
            RegexOptions.Compiled);



    private static readonly Regex StructuredLoggerRx =
        new(
            @"\b(Log(ger)?\s*\.\s*(Trace|Debug|Information|Info|Warn|Warning|Error|Fatal)|logger\.log)\s*\(",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    private static readonly Regex InterpolationRx =
        new(
            @"(\$""|f""|format\(|string\.Format)",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    public LoggingEvaluator(
        ILogger<LoggingEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Logging ?? new LoggingPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();


        if (!_policy.Enabled)
        {
            _logger.LogInformation(
                "⏭ {Evaluator} disabled by policy.",
                Name);

            return results;
        }



        var files =
            Directory.EnumerateFiles(
                    projectPath,
                    "*.*",
                    SearchOption.AllDirectories)
                .Where(IsSupportedSourceFile)
                .Where(f => !IsExcludedDir(f))
                .ToList();



        _logger.LogInformation(
            "🧩 Running {Evaluator} on {Count} source files",
            Name,
            files.Count);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            string content;

            try
            {
                content =
                    await File.ReadAllTextAsync(
                        file,
                        token);
            }
            catch
            {
                continue;
            }



            var language =
                DetectLanguage(file);



            double structuredScore =
                EvaluateStructuredLogging(
                    content,
                    language);



            double levelScore =
                EvaluateLogLevels(
                    content);



            double securityScore =
                EvaluateSensitiveDataExposure(
                    content);



            double noiseScore =
                EvaluateLoggingNoise(
                    content);



            double integrityScore =
                ComputeIntegrity(
                    structuredScore,
                    levelScore,
                    securityScore,
                    noiseScore);



            results.Add(
                new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "Infrastructure",

                    Metrics = new Dictionary<string, double>
                    {
                        ["StructuredLoggingScore"] = structuredScore,

                        ["LogLevelComplianceScore"] = levelScore,

                        ["SensitiveDataProtectionScore"] = securityScore,

                        ["LoggingNoiseScore"] = noiseScore,

                        ["LoggingIntegrityScore"] = integrityScore
                    },


                    Metadata = new Dictionary<string, string>
                    {
                        ["FileName"] =
                            Path.GetFileName(file),

                        ["Target"] =
                            file,

                        ["Language"] =
                            language,

                        ["Frameworks"] =
                            string.Join(
                                ", ",
                                DetectFrameworks(language)),

                        ["PolicyEnabled"] =
                            _policy.Enabled.ToString()
                    }
                });
        }



        if (results.Count > 0)
        {
            int analyzedFiles =
                results.Count;



            double avgIntegrity =
                results.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "LoggingIntegrityScore",
                            0));



            double avgStructured =
                results.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "StructuredLoggingScore",
                            0));



            double avgSecurity =
                results.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "SensitiveDataProtectionScore",
                            0));



            results.Add(
                new ArchitectureEvaluatorResult(Name, projectPath)
                {
                    Category = "InfrastructureSummary",

                    Metrics = new Dictionary<string, double>
                    {
                        ["AnalyzedFiles"] =
                            analyzedFiles,


                        ["AverageIntegrityScore"] =
                            avgIntegrity,


                        ["AverageStructuredLogging"] =
                            avgStructured,


                        ["AverageSecurityProtection"] =
                            avgSecurity,


                        ["ObservabilityHealthIndex"] =
                            avgIntegrity * 0.55 +
                            avgStructured * 0.25 +
                            avgSecurity * 0.20
                    },


                    Metadata = new Dictionary<string, string>
                    {
                        ["Evaluator"] = Name,

                        ["PolicyEnabled"] =
                            _policy.Enabled.ToString(),

                        ["SupportedFrameworks"] =
                            string.Join(
                                ", ",
                                SupportedFrameworks)
                    }
                });
        }



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} metric entries",
            Name,
            results.Count);


        return results;
    }



    private double EvaluateStructuredLogging(
        string content,
        string language)
    {
        if (!_policy.RequireStructuredLogging)
            return 1;


        if (!_policy.FrameworkHints.TryGetValue(
                language,
                out var frameworks))
        {
            return 0.5;
        }


        return frameworks.Any(
                fw =>
                    content.Contains(
                        fw,
                        StringComparison.OrdinalIgnoreCase))
            ? 1
            : StructuredLoggerRx.IsMatch(content)
                ? 0.75
                : 0;
    }



    private double EvaluateLogLevels(
        string content)
    {
        if (!_policy.RequireLogLevels)
            return 1;


        return LogLevelRx.IsMatch(content)
            ? 1
            : 0.5;
    }



    private double EvaluateSensitiveDataExposure(
        string content)
    {
        if (!_policy.DetectHardcodedSecretsInLogs)
            return 1;


        return SensitiveLoggingRx.IsMatch(content)
            ? 0
            : 1;
    }



    private double EvaluateLoggingNoise(
        string content)
    {
        if (!_policy.WarnInterpolatedStrings)
            return 1;


        int count =
            InterpolationRx.Matches(content)
                .Count;


        if (count <= 5)
            return 1;


        return Math.Max(
            0,
            1 -
            count / 20.0);
    }



    private static double ComputeIntegrity(
        double structured,
        double levels,
        double security,
        double noise)
    {
        return Math.Round(
            (
                structured * 0.35 +
                levels * 0.20 +
                security * 0.30 +
                noise * 0.15
            ) * 100,
            2);
    }



    private static bool IsSupportedSourceFile(
        string file)
    {
        return
            file.EndsWith(".cs",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".java",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".py",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".ts",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".js",
                StringComparison.OrdinalIgnoreCase);
    }



    private static string DetectLanguage(
        string path)
    {
        if (path.EndsWith(".cs",
            StringComparison.OrdinalIgnoreCase))
            return "C#";


        if (path.EndsWith(".java",
            StringComparison.OrdinalIgnoreCase))
            return "Java";


        if (path.EndsWith(".py",
            StringComparison.OrdinalIgnoreCase))
            return "Python";


        if (path.EndsWith(".ts",
                StringComparison.OrdinalIgnoreCase)
            ||
            path.EndsWith(".js",
                StringComparison.OrdinalIgnoreCase))
            return "Node";


        return "Unknown";
    }



    private IEnumerable<string> DetectFrameworks(
        string language)
    {
        if (_policy.FrameworkHints.TryGetValue(
                language,
                out var frameworks))
        {
            return frameworks;
        }


        return Array.Empty<string>();
    }



    private static readonly Regex LogLevelRx =
        new(
            @"\b(Log(ger)?\s*\.\s*(Trace|Debug|Information|Info|Warn|Warning|Error|Fatal)|logger\.log)\s*\(",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);
}