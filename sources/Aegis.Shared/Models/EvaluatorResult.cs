using Aegis.Shared.Enums;
using Aegis.Shared.Models.Rules;

namespace Aegis.Shared.Models;

/// <summary>
/// Represents the result of a single evaluator’s execution, including
/// raw metrics, rule outcomes, contextual domain information,
/// and AI-ready insights.
/// </summary>
public sealed class EvaluatorResult
{
    // 🔖 Identification
    public string Source { get; set; } = string.Empty;        // e.g. "ComplexityEvaluator"
    public string Target { get; set; } = string.Empty;        // e.g. "Services/UserService.cs"

    // 🧭 Context
    public string? Domain { get; set; }                       // e.g. Architecture / Backend / Naming
    public string? Category { get; set; }                     // e.g. "DesignPatterns", "Dependency"
    public string? Namespace { get; set; }                    // Logical grouping for type-based analysis
    public string? Layer { get; set; }                        // e.g. "App", "Domain", "Infrastructure"

    // 📊 Raw metrics
    public Dictionary<string, double> Metrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string>? Metadata { get; set; } // e.g. line numbers, function names, file paths

    // 🧩 Evaluated rules (core to aggregation)
    public List<RuleResult> RuleResults { get; set; } = new();

    // ⚖️ Evaluator influence (policy-level weighting)
    /// <summary>
    /// Relative importance of this evaluator in the global audit (default 1.0).
    /// Applied during aggregation and scoring.
    /// </summary>
    public double WeightFactor { get; set; } = 1.0;

    /// <summary>
    /// Whether the evaluator was enabled or disabled in the active policy.
    /// </summary>
    public bool WasEnabled { get; set; } = true;

    // 🧮 Derived scoring info (for diagnostics & reports)
    public double DomainWeightedCompliance { get; set; }      // Computed after RuleWeightingEngine
    public double DomainMaintainabilityIndex { get; set; }    // Aggregated maintainability metric
    public double DomainHealthIndex { get; set; }             // Aggregated health metric
    public double DomainResilienceIndex { get; set; }         // Aggregated domain stability

    // 🧠 Policy traceability
    public string? PolicyVersion { get; set; }                // Policy version applied for this evaluation
    public string? RuleSetVersion { get; set; }               // Specific ruleset revision
    public string? AegisEngineVersion { get; set; }           // Helps compare scans over time

    // 💬 Human/AI insight layer
    public string? Summary { get; set; }                      // Short textual summary of domain findings
    public List<string> Recommendations { get; set; } = new(); // Localized or AI-generated recommendations
    public List<string> Anomalies { get; set; } = new();       // Detected anomalies (used in reports)
    public bool ContainsCriticalViolations => RuleResults.Any(r => r.Severity >= RuleSeverity.Critical);

    // 🧾 Constructors
    public EvaluatorResult() { }

    public EvaluatorResult(string source, string target, string? domain = null)
    {
        Source = source;
        Target = target;
        Domain = domain ?? "General";
    }

    // 🧩 Methods
    public void AddMetric(string name, double value) => Metrics[name] = value;

    public void AddRuleResult(RuleResult rule)
    {
        RuleResults.Add(rule);
        if (rule.Severity == RuleSeverity.Blocker)
            Anomalies.Add($"Blocker rule triggered: {rule.RuleId} ({rule.RuleName})");
    }

    public override string ToString()
    {
        var metricsSummary = Metrics.Any()
            ? string.Join(", ", Metrics.Select(m => $"{m.Key}={m.Value:0.##}"))
            : "No metrics";
        var rulesCount = RuleResults.Count;
        return $"{Source} → {Target} [{Domain}] | Weight={WeightFactor:0.##} | {metricsSummary} | {rulesCount} rules evaluated";
    }
}
