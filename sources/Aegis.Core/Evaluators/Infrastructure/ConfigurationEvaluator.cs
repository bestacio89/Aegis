using System.Text.Json;
using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using YamlDotNet.RepresentationModel;

namespace Aegis.Architecture.Evaluators.Infrastructure;

/// <summary>
/// Cross-platform configuration architecture evaluator.
/// Evaluates configuration hygiene across application, container,
/// CI/CD, Kubernetes, and dependency management environments.
///
/// Produces:
/// - SyntaxComplianceScore
/// - SecretExposureScore
/// - InfrastructureSafetyScore
/// - DependencyPinningScore
/// - ConfigurationIntegrityScore
///
/// Aggregates into InfrastructureHealthIndex.
/// </summary>
public sealed class ConfigurationEvaluator : BaseArchitectureEvaluator
{
    private readonly ConfigurationPolicy _policy;

    public override string Name => "ConfigurationEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "JavaScript",
        "TypeScript"
    ];

    public override string[] SupportedFrameworks =>
    [
        "Docker",
        "Kubernetes",
        "CI/CD",
        "Environment",
        "PackageManagement"
    ];


    private static readonly Regex SecretAssignmentRx =
        new(
            @"(?i)(password|passwd|secret|token|apikey|api_key|connectionstring|privatekey)\s*[:=]\s*['""]?([^'""\s]+)",
            RegexOptions.Compiled);


    private static readonly Regex UnsafeDockerCommandRx =
        new(
            @"\b(chmod\s+777|curl\s+.*\|\s*sh|wget\s+.*\|\s*sh|rm\s+-rf\s+/|apt-get\s+install)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);


    private static readonly Regex UnsafePipelineCommandRx =
        new(
            @"\b(sudo|chmod\s+777|curl\s+.*\|\s*sh|wget\s+.*\|\s*sh|bash\s+-c)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);


    public ConfigurationEvaluator(
        ILogger<ConfigurationEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Configuration ?? new ConfigurationPolicy();
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


        var configurationFiles =
            Directory.EnumerateFiles(
                    projectPath,
                    "*.*",
                    SearchOption.AllDirectories)
                .Where(IsConfigurationFile)
                .Where(f => !IsExcludedDir(f))
                .ToList();


        _logger.LogInformation(
            "🌍 Running {Evaluator} on {Count} configuration files",
            Name,
            configurationFiles.Count);


        foreach (var file in configurationFiles)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch
            {
                continue;
            }


            double syntaxScore = EvaluateSyntax(file, content);
            double secretScore = EvaluateSecrets(content);
            double safetyScore = EvaluateInfrastructureSafety(file, content);
            double dependencyScore = EvaluateDependencyDiscipline(file, content);


            double integrityScore =
                ComputeIntegrity(
                    syntaxScore,
                    secretScore,
                    safetyScore,
                    dependencyScore);


            results.Add(
                new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = DetectCategory(file),

                    Metrics = new Dictionary<string, double>
                    {
                        ["SyntaxComplianceScore"] = syntaxScore,
                        ["SecretExposureScore"] = secretScore,
                        ["InfrastructureSafetyScore"] = safetyScore,
                        ["DependencyPinningScore"] = dependencyScore,
                        ["ConfigurationIntegrityScore"] = integrityScore
                    },

                    Metadata = new Dictionary<string, string>
                    {
                        ["FileName"] = Path.GetFileName(file),
                        ["FileType"] = Path.GetExtension(file),
                        ["Language"] = Context?.Language ?? "Unknown",
                        ["Framework"] = DetectFramework(file),
                        ["Target"] = file,
                        ["PolicyEnabled"] = _policy.Enabled.ToString()
                    }
                });
        }


        if (results.Count > 0)
        {
            var evaluatedCount = results.Count;


            double avgIntegrity =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "ConfigurationIntegrityScore",
                        0));


            double avgSyntax =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "SyntaxComplianceScore",
                        0));


            double avgSecrets =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "SecretExposureScore",
                        0));


            double avgSafety =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "InfrastructureSafetyScore",
                        0));


            double avgDependencies =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "DependencyPinningScore",
                        0));


            results.Add(
                new ArchitectureEvaluatorResult(Name, projectPath)
                {
                    Category = "InfrastructureSummary",

                    Metrics = new Dictionary<string, double>
                    {
                        ["ConfigurationFileCount"] = evaluatedCount,

                        ["AverageIntegrityScore"] = avgIntegrity,

                        ["AverageSyntaxCompliance"] = avgSyntax,

                        ["AverageSecretProtection"] = avgSecrets,

                        ["AverageInfrastructureSafety"] = avgSafety,

                        ["AverageDependencyDiscipline"] = avgDependencies,


                        ["InfrastructureHealthIndex"] =
                            avgIntegrity * 0.5 +
                            avgSafety * 0.3 +
                            avgSecrets * 0.2
                    },

                    Metadata = new Dictionary<string, string>
                    {
                        ["Evaluator"] = Name,
                        ["PolicyEnabled"] =
                            _policy.Enabled.ToString(),

                        ["CheckedFrameworks"] =
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



    private double EvaluateSyntax(
        string file,
        string content)
    {
        if (!_policy.ValidateSyntax)
            return 1;


        try
        {
            if (file.EndsWith(".json",
                StringComparison.OrdinalIgnoreCase))
            {
                JsonDocument.Parse(content);
            }


            if (file.EndsWith(".yaml",
                    StringComparison.OrdinalIgnoreCase)
                ||
                file.EndsWith(".yml",
                    StringComparison.OrdinalIgnoreCase))
            {
                var yaml = new YamlStream();
                yaml.Load(new StringReader(content));
            }


            return 1;
        }
        catch
        {
            return 0;
        }
    }



    private double EvaluateSecrets(string content)
    {
        if (_policy.ForbiddenKeys.Length == 0)
            return 1;


        foreach (var forbidden in _policy.ForbiddenKeys)
        {
            if (content.Contains(
                    forbidden,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
        }


        return SecretAssignmentRx.IsMatch(content)
            ? 0.25
            : 1;
    }



    private double EvaluateInfrastructureSafety(
        string file,
        string content)
    {
        if (!_policy.CheckInfrastructureConfigs)
            return 1;


        if (file.Contains(
                "Dockerfile",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.Contains(
                "docker-compose",
                StringComparison.OrdinalIgnoreCase))
        {
            return UnsafeDockerCommandRx.IsMatch(content)
                ? 0.5
                : 1;
        }


        if (file.Contains(
                ".github",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.Contains(
                ".gitlab",
                StringComparison.OrdinalIgnoreCase))
        {
            return UnsafePipelineCommandRx.IsMatch(content)
                ? 0.5
                : 1;
        }


        return 1;
    }



    private static double EvaluateDependencyDiscipline(
        string file,
        string content)
    {
        if (!file.Contains(
                "requirements",
                StringComparison.OrdinalIgnoreCase)
            &&
            !file.Contains(
                "package",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }


        var lines =
            content.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);


        if (lines.Length == 0)
            return 1;


        var unpinned =
            lines.Count(
                line =>
                    !line.StartsWith("#")
                    &&
                    !line.Contains("==")
                    &&
                    !line.Contains("@")
                    &&
                    !line.Contains("^"));


        return Math.Max(
            0,
            1 -
            (double)unpinned / lines.Length);
    }



    private static double ComputeIntegrity(
        double syntax,
        double secrets,
        double safety,
        double dependencies)
    {
        return Math.Round(
            (
                syntax * 0.3 +
                secrets * 0.3 +
                safety * 0.25 +
                dependencies * 0.15
            ) * 100,
            2);
    }



    private static bool IsConfigurationFile(string file)
    {
        var normalized =
            file.Replace(
                '\\',
                '/');


        return
            file.EndsWith(".json",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".yaml",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".yml",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".env",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".properties",
                StringComparison.OrdinalIgnoreCase)
            ||
            Path.GetFileName(file)
                .Equals(
                    "Dockerfile",
                    StringComparison.OrdinalIgnoreCase)
            ||
            normalized.Contains(".github/workflows")
            ||
            normalized.Contains(".gitlab-ci.yml");
    }



    private static string DetectCategory(string file)
    {
        if (file.Contains(
                "Docker",
                StringComparison.OrdinalIgnoreCase))
            return "Container";


        if (file.Contains(
                ".github",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.Contains(
                ".gitlab",
                StringComparison.OrdinalIgnoreCase))
            return "Pipeline";


        if (file.EndsWith(".yaml",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".yml",
                StringComparison.OrdinalIgnoreCase))
            return "Kubernetes";


        if (file.EndsWith(".env",
                StringComparison.OrdinalIgnoreCase))
            return "Environment";


        return "Configuration";
    }



    private static string DetectFramework(string file)
    {
        if (file.Contains(
                "Docker",
                StringComparison.OrdinalIgnoreCase))
            return "Docker";


        if (file.Contains(
                ".github",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.Contains(
                ".gitlab",
                StringComparison.OrdinalIgnoreCase))
            return "CI/CD";


        if (file.EndsWith(".yaml",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".yml",
                StringComparison.OrdinalIgnoreCase))
            return "Kubernetes";


        if (file.EndsWith(".env",
                StringComparison.OrdinalIgnoreCase))
            return "Environment";


        return "Generic";
    }
}