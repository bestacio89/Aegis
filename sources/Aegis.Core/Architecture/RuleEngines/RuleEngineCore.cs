using Microsoft.Extensions.Logging;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Core.Architecture.RuleEngines;

/// <summary>
/// 🧠 Interprets evaluator metrics (facts) into rule violations using policy-aware thresholds
/// while safely falling back to static rule registry defaults.
/// </summary>
public sealed class RuleEngineCore
{
    private readonly ILogger<RuleEngineCore> _logger;
    private readonly IReadOnlyList<ArchitectureRuleDefinition> _rules;
    private AegisArchitecturePolicy _policy;

    public RuleEngineCore(ILogger<RuleEngineCore> logger, AegisArchitecturePolicy policy)
    {
        _logger = logger;
        _policy = policy;
        _rules = ArchitectureRuleRegistry.All;
    }

    // ======================================================
    // 🧩 Policy synchronization
    // ======================================================
    /// <summary>
    /// Applies a new runtime policy and synchronizes it with all active rule definitions.
    /// </summary>
    public void ApplyPolicy(AegisArchitecturePolicy newPolicy)
    {
        _policy = newPolicy;
        _logger.LogInformation("📜 Aegis policy applied → {Name} (v{Version})",
            newPolicy.Name ?? "Unnamed", newPolicy.Version ?? "1.0");

        // Optionally, push updated thresholds into RuleRegistry
        foreach (var rule in ArchitectureRuleRegistry.All)
        {
            var updatedThreshold = ResolveThreshold(rule);
            if (Math.Abs(rule.Threshold - updatedThreshold) > 0.0001)
            {
                _logger.LogDebug("🔧 Rule {Id} threshold updated: {Old} → {New}",
                    rule.Id, rule.Threshold, updatedThreshold);
                rule.Threshold = updatedThreshold;
            }
        }
    }

    // ======================================================
    // 🧮 Evaluation
    // ======================================================
    public IEnumerable<ArchitectureRuleresult> Evaluate(ProjectArchitectureContext context, IEnumerable<ArchitectureEvaluatorResult> facts)
    {
        var results = new List<ArchitectureRuleresult>();
        int totalChecks = 0;

        foreach (var fact in facts)
        {
            foreach (var (metricKey, value) in fact.Metrics)
            {
                var applicable = _rules.Where(r =>
                    r.MetricKey.Equals(metricKey, StringComparison.OrdinalIgnoreCase));

                foreach (var rule in applicable)
                {
                    totalChecks++;

                    double effectiveThreshold = ResolveThreshold(rule);

                    bool violated = rule.Operator switch
                    {
                        ComparisonOperator.LessThan => value < effectiveThreshold,
                        ComparisonOperator.GreaterThan => value > effectiveThreshold,
                        ComparisonOperator.Equal => Math.Abs(value - effectiveThreshold) < 0.0001,
                        ComparisonOperator.NotEqual => Math.Abs(value - effectiveThreshold) > 0.0001,
                        _ => false
                    };

                    if (violated)
                    {
                        var category = Enum.TryParse<ArchitectureRuleCategory>(rule.Category, true, out var cat)
                            ? cat
                            : ArchitectureRuleCategory.General;

                        results.Add(new ArchitectureRuleresult(
                            rule.Id,
                            rule.Name,
                            category,
                            rule.Severity,
                            fact.Target,
                            fact.Metadata?.GetValueOrDefault("Namespace"),
                            $"[{fact.Source}] Metric '{metricKey}' = {value:0.##}, threshold = {effectiveThreshold:0.##}. {rule.Recommendation}",
                            DateTimeOffset.UtcNow,
                            isCompliant: false
                        )
                        {
                            DetectedBy = fact.Source,
                            Domain = context.DomainType ?? "General",
                            AnalyzerVersion = context.DetectorVersion
                        });
                    }
                }
            }
        }

        _logger.LogInformation(
            "🧩 RuleEngineCore evaluated {Facts} facts → {Results} violations ({Checks} checks).",
            facts.Count(), results.Count, totalChecks);

        return results;
    }

    // ======================================================
    // 🎚️ Threshold resolution
    // ======================================================
    private double ResolveThreshold(ArchitectureRuleDefinition rule)
    {
        try
        {
            return rule.Category switch
            {
                nameof(ArchitectureRuleCategory.Performance) => _policy.Performance?.MaxNestedLoopDepth
                                                    ?? rule.Threshold,

                nameof(ArchitectureRuleCategory.Maintainability) => _policy.Maintainability?.MinMaintainabilityIndex
                                                        ?? rule.Threshold,

                nameof(ArchitectureRuleCategory.Security) => _policy.Security?.MinimumScore
                                                 ?? rule.Threshold,

                nameof(ArchitectureRuleCategory.Dependency) => _policy.Dependency?.MaxDependencyDepth
                                                   ?? rule.Threshold,

                nameof(ArchitectureRuleCategory.Coupling) => _policy.Coupling?.MaxCouplingRatio
                                                 ?? rule.Threshold,

                nameof(ArchitectureRuleCategory.Architecture) => _policy.Architecture?.AllowedDependencies?.Count
                                                     ?? rule.Threshold,

                _ => rule.Threshold
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to resolve threshold for rule {RuleId}, using default {Threshold}.", rule.Id, rule.Threshold);
            return rule.Threshold;
        }
    }
}
