using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Naming;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Naming;

/// <summary>
/// 🧭 Evaluates naming consistency, style conformity, and semantic coherence
/// across classes, methods, variables, and files according to the NamingPolicy.
/// </summary>
public sealed class NamingEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly NamingPolicy _policy;

    public override string Name => "Naming Convention Evaluator";
    public override string[] SupportedLanguages => ["CSharp", "Python", "TypeScript", "Java"];
    public override string[] SupportedFrameworks => ["DotNet", "Node", "Spring"];

    public NamingEvaluator(ILogger<NamingEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Naming ?? new NamingPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.Enabled)
        {
            _logger.LogInformation("⏭ {Evaluator} disabled by policy.", Name);
            return results;
        }

        _logger.LogInformation("🔍 Running {Evaluator} on {Path}", Name, projectPath);

        var codeFiles = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f =>
                f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".java", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        foreach (var file in codeFiles)
        {
            token.ThrowIfCancellationRequested();

            string content;
            try { content = await File.ReadAllTextAsync(file, token); }
            catch { continue; }

            var result = new ArchitectureEvaluatorResult(Name, Path.GetFileName(file))
            {
                Category = nameof(ArchitectureRuleCategory.Naming),
                Metrics = new Dictionary<string, double>(),
                Metadata = new Dictionary<string, string> { ["FilePath"] = file }
            };

            // ===============================================================
            // 1️⃣ Class and Interface Naming Conventions
            // ===============================================================
            var classNames = Regex.Matches(content, @"\b(class|interface)\s+([A-Za-z_][A-Za-z0-9_]*)");
            var pascalViolations = classNames.Count(m => !Regex.IsMatch(m.Groups[2].Value, @"^[A-Z][a-zA-Z0-9]+$"));
            var prefixViolations = classNames.Count(m => _policy.RequireInterfacePrefix && m.Groups[1].Value == "interface" && !m.Groups[2].Value.StartsWith("I"));

            result.Metrics["ClassNamingViolationCount"] = pascalViolations;
            result.Metrics["InterfacePrefixViolationCount"] = prefixViolations;
            result.Metrics["ClassNamingComplianceIndex"] = classNames.Count == 0 ? 100 : Math.Max(0, 100 - pascalViolations * 100.0 / classNames.Count);

            // ===============================================================
            // 2️⃣ Method Naming Conventions
            // ===============================================================
            var methodNames = Regex.Matches(content, @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(")
                                   .Cast<Match>()
                                   .Select(m => m.Groups[1].Value)
                                   .Where(n => !new[] { "if", "for", "while", "switch" }.Contains(n))
                                   .ToList();

            var camelViolations = methodNames.Count(n => !Regex.IsMatch(n, @"^[a-z][a-zA-Z0-9]+$"));
            result.Metrics["MethodNamingViolationCount"] = camelViolations;
            result.Metrics["MethodNamingComplianceIndex"] = methodNames.Count == 0 ? 100 : Math.Max(0, 100 - camelViolations * 100.0 / methodNames.Count);

            // ===============================================================
            // 3️⃣ Variable and Constant Naming
            // ===============================================================
            var variableNames = Regex.Matches(content, @"\b(var|let|const|int|float|double|string|bool|decimal)\s+([A-Za-z_][A-Za-z0-9_]*)")
                                     .Cast<Match>()
                                     .Select(m => m.Groups[2].Value)
                                     .ToList();

            var constantViolations = variableNames.Count(v => v.All(char.IsUpper) && !_policy.AllowAllCapsConstants);
            var snakeViolations = variableNames.Count(v => v.Contains("_") && !_policy.AllowSnakeCase);

            result.Metrics["VariableNamingViolationCount"] = snakeViolations + constantViolations;
            result.Metrics["VariableNamingComplianceIndex"] = variableNames.Count == 0 ? 100 :
                Math.Max(0, 100 - (snakeViolations + constantViolations) * 100.0 / variableNames.Count);

            // ===============================================================
            // 4️⃣ File Name and Pluralization
            // ===============================================================
            var fileName = Path.GetFileNameWithoutExtension(file);
            var pluralViolation = _policy.EnforcePluralization &&
                                  (fileName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) ||
                                   fileName.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
                                  && fileName.EndsWith("s", StringComparison.OrdinalIgnoreCase);

            result.Metrics["FilePluralizationViolation"] = pluralViolation ? 1 : 0;

            // ===============================================================
            // ✅ Consolidate Results
            // ===============================================================
            result.Metrics["OverallNamingComplianceIndex"] =
                (result.Metrics["ClassNamingComplianceIndex"] +
                 result.Metrics["MethodNamingComplianceIndex"] +
                 result.Metrics["VariableNamingComplianceIndex"]) / 3;

            results.Add(result);
        }

        _logger.LogInformation("✅ {Evaluator} completed with {Count} analyzed files", Name, results.Count);
        return results;
    }
}
