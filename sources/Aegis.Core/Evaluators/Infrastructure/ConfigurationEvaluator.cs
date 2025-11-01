using System.Text.Json;
using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;
using Aegis.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YamlDotNet.RepresentationModel;

namespace Aegis.Architecture.Evaluators.Infrastructure;

/// <summary>
/// Cross-language configuration hygiene evaluator for .NET, Java, Python, Node.js, and DevOps environments.
/// Quantifies configuration health via syntax integrity, secret exposure, dependency control, and CI/CD safety.
/// Produces ConfigurationIntegrityScore (0–100) and aggregates InfrastructureHealthIndex.
/// </summary>
public sealed class ConfigurationEvaluator : BaseArchitectureEvaluator
{
    private readonly ConfigurationPolicy _policy;

    public override string Name => "ConfigurationEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Java", "Python", "JavaScript", "TypeScript"];
    public override string[] SupportedFrameworks => ["Docker", "Kubernetes", "CI/CD", "Infra"];

    private static readonly Regex EnvLineRx = new(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=", RegexOptions.Multiline);
    private static readonly Regex KeyValueRx = new(@"^\s*([A-Za-z0-9_.-]+)\s*[:=]\s*['""]?.+['""]?", RegexOptions.Multiline);
    private static readonly Regex UnsafeDockerCmdRx = new(@"\b(chmod|curl|wget|apt-get install|rm -rf)\b", RegexOptions.IgnoreCase);
    private static readonly Regex UnsafePipelineCmdRx = new(@"\b(sudo|chmod|curl|wget|bash -c)\b", RegexOptions.IgnoreCase);

    public ConfigurationEvaluator(ILogger<ConfigurationEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Configuration ?? new ConfigurationPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.Enabled)
        {
            _logger.LogInformation("⏭ {Evaluator} disabled by policy.", Name);
            return results;
        }

        var configFiles = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f =>
                f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".env", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".properties", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("docker-compose", StringComparison.OrdinalIgnoreCase) ||
                f.Contains(".github/workflows") || f.Contains(".gitlab-ci.yml"))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        _logger.LogInformation("🌍 Running {Evaluator} on {Count} configuration files", Name, configFiles.Count);

        foreach (var file in configFiles)
        {
            token.ThrowIfCancellationRequested();
            string content;
            try { content = await File.ReadAllTextAsync(file, token); }
            catch { continue; }

            // Base evaluation scores
            double syntaxScore = 1.0;
            double secretScore = 1.0;
            double safetyScore = 1.0;
            double dependencyScore = 1.0;

            // 🧩 Syntax checks
            if (_policy.ValidateSyntax)
            {
                if (file.EndsWith(".json"))
                {
                    try { JsonDocument.Parse(content); }
                    catch { syntaxScore = 0.0; }
                }
                else if (file.EndsWith(".yaml") || file.EndsWith(".yml"))
                {
                    try { var yaml = new YamlStream(); yaml.Load(new StringReader(content)); }
                    catch { syntaxScore = 0.0; }
                }
            }

            // 🧩 Secret detection
            if (_policy.ForbiddenKeys.Any(k => content.Contains(k, StringComparison.OrdinalIgnoreCase)))
                secretScore = 0.0;

            // 🧩 Docker / Pipeline safety
            if (_policy.CheckInfrastructureConfigs)
            {
                if ((file.Contains("Dockerfile") || file.Contains("docker-compose")) && UnsafeDockerCmdRx.IsMatch(content))
                    safetyScore = 0.5;

                if ((file.Contains(".github") || file.Contains(".gitlab")) && UnsafePipelineCmdRx.IsMatch(content))
                    safetyScore = 0.5;
            }

            // 🧩 Python dependency hygiene (requirements.txt)
            if (file.Contains("requirements", StringComparison.OrdinalIgnoreCase))
            {
                var lines = content.Split('\n');
                int unpinned = lines.Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#") && !l.Contains("=="));
                dependencyScore = unpinned > 0 ? Math.Max(0, 1 - (double)unpinned / lines.Length) : 1.0;
            }

            // Compute configuration compliance
            double integrityScore = ComputeIntegrity(syntaxScore, secretScore, safetyScore, dependencyScore);

            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Infrastructure",
                Metrics = new Dictionary<string, double>
                {
                    ["SyntaxScore"] = syntaxScore,
                    ["SecretScore"] = secretScore,
                    ["SafetyScore"] = safetyScore,
                    ["DependencyScore"] = dependencyScore,
                    ["ConfigurationIntegrityScore"] = integrityScore
                },
                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = Path.GetFileName(file),
                    ["FileType"] = Path.GetExtension(file),
                    ["Framework"] = DetectFramework(file),
                    ["PolicyEnabled"] = _policy.Enabled.ToString()
                }
            });
        }

        // 📊 Aggregate summary
        if (results.Count > 0)
        {
            double avgIntegrity = results.Average(r => r.Metrics.GetValueOrDefault("ConfigurationIntegrityScore", 0));
            double avgSafety = results.Average(r => r.Metrics.GetValueOrDefault("SafetyScore", 0));
            double avgSecret = results.Average(r => r.Metrics.GetValueOrDefault("SecretScore", 0));

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "InfrastructureSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["ConfigFileCount"] = results.Count,
                    ["AverageIntegrityScore"] = avgIntegrity,
                    ["AverageSafetyScore"] = avgSafety,
                    ["AverageSecretScore"] = avgSecret,
                    ["InfrastructureHealthIndex"] = avgIntegrity * 0.7 + avgSafety * 0.2 + avgSecret * 0.1
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] = _policy.Enabled.ToString(),
                    ["CheckedFrameworks"] = string.Join(", ", SupportedFrameworks)
                }
            });
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} results", Name, results.Count);
        return results;
    }

    private static double ComputeIntegrity(double syntax, double secret, double safety, double dependency)
    {
        // Weighted scoring emphasizing correctness and safety
        double score = syntax * 0.3 + secret * 0.3 + safety * 0.25 + dependency * 0.15;
        return Math.Round(score * 100, 2);
    }

    private static string DetectFramework(string file)
    {
        if (file.Contains("Dockerfile", StringComparison.OrdinalIgnoreCase) || file.Contains("docker-compose"))
            return "Docker";
        if (file.Contains(".github") || file.Contains(".gitlab"))
            return "CI/CD";
        if (file.EndsWith(".yaml") || file.EndsWith(".yml"))
            return "Kubernetes";
        if (file.EndsWith(".env") || file.EndsWith(".properties"))
            return "Environment";
        if (file.Contains("requirements", StringComparison.OrdinalIgnoreCase))
            return "Python";
        return "Generic";
    }
}
