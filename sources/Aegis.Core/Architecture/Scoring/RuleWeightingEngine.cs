using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Models;
using Microsoft.Extensions.Logging;
using System;

namespace Aegis.Core.Architecture.Scoring;

/// <summary>
/// Applies policy-driven weighted scoring to <see cref="ArchitectureRuleresult"/> objects
/// based on severity, maintainability, and performance policies.
/// </summary>
public sealed class RuleWeightingEngine
{
    private readonly ILogger<RuleWeightingEngine> _logger;
    private readonly Dictionary<ArchitectureRuleSeverity, double> _baseWeights = new()
    {
        [ArchitectureRuleSeverity.Critical] = 1.5,
        [ArchitectureRuleSeverity.High] = 1.25,
        [ArchitectureRuleSeverity.Medium] = 1.0,
        [ArchitectureRuleSeverity.Low] = 0.75,
        [ArchitectureRuleSeverity.Info] = 0.5
    };

    private double _performanceScale = 1.0;
    private double _maintainabilityFactor = 1.0;
    private double _minimumImpactThreshold = 0.05;

    public RuleWeightingEngine(ILogger<RuleWeightingEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Applies contextual weights to each rule result.
    /// </summary>
    public IEnumerable<ArchitectureRuleresult> ApplyWeights(IEnumerable<ArchitectureRuleresult> results)
    {
        foreach (var result in results)
        {
            // Base weight from severity
            var baseWeight = _baseWeights.GetValueOrDefault(result.Severity, 1.0);

            // Apply category-specific scaling
            double adjustedWeight = baseWeight;

            if (string.Equals(result.Category.ToString(), "Performance", StringComparison.OrdinalIgnoreCase))
                adjustedWeight *= _performanceScale;

            if (string.Equals(result.Category.ToString(), "Maintainability", StringComparison.OrdinalIgnoreCase))
                adjustedWeight *= _maintainabilityFactor;

            // Enforce threshold (ignore micro-violations)
            if (adjustedWeight < _minimumImpactThreshold)
                adjustedWeight = 0;

            result.WeightedImpact = result.IsCompliant ? 0 : adjustedWeight;

            yield return result;
        }
    }

    /// <summary>
    /// Computes normalized compliance according to weighted impacts.
    /// </summary>
    public double ComputeWeightedCompliance(IEnumerable<ArchitectureRuleresult> results)
    {
        var ruleList = results.ToList();
        if (ruleList.Count == 0)
            return 1.0;

        var weightedFailures = ruleList.Sum(r => r.WeightedImpact);
        var totalWeight = ruleList.Count;
        var score = 1.0 - weightedFailures / Math.Max(1, totalWeight);

        return Math.Clamp(score, 0, 1);
    }

    // =========================================================
    // 🧭 Policy integration
    // =========================================================

    /// <summary>
    /// Dynamically applies Aegis policy values to this engine.
    /// </summary>
    public void ApplyPolicy(AegisArchitecturePolicy policy)
    {
        try
        {
            _performanceScale = policy.Performance.PerformanceWeightScale;
            _minimumImpactThreshold = policy.Performance.MinimumPerformanceImpactThreshold;
            _maintainabilityFactor = policy.Maintainability.Factor;

            _logger.LogInformation(
                "RuleWeightingEngine policy applied: PerformanceScale={Perf}, MaintainabilityFactor={Maint}, Threshold={Thr}",
                _performanceScale, _maintainabilityFactor, _minimumImpactThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply policy to RuleWeightingEngine; defaults retained.");
        }
    }
}
