using Aegis.Shared.Models;
using Aegis.Shared.Models.Rules;
using Aegis.Shared.Models.Policies;
using Aegis.Core.Scoring;
using Microsoft.Extensions.Logging;

namespace Aegis.Core.Aggregation;

/// <summary>
/// Aggregates evaluator results into deterministic, weighted, and normalized
/// compliance metrics at both domain and global levels.
/// </summary>
public sealed class CrossEvaluatorAggregator
{
    private readonly ILogger<CrossEvaluatorAggregator> _logger;
    private readonly RuleWeightingEngine _weightingEngine;

    // 🔧 Policy state
    private AegisPolicy? _policy;

    // Global modifiers from policy
    private double _globalHealthWeight = 1.0;
    private double _maintainabilityFactor = 1.0;
    private double _resilienceSensitivity = 1.0;

    public CrossEvaluatorAggregator(
        ILogger<CrossEvaluatorAggregator> logger,
        RuleWeightingEngine weightingEngine)
    {
        _logger = logger;
        _weightingEngine = weightingEngine;
    }

    // ==============================================================
    // 🧭 POLICY APPLICATION
    // ==============================================================

    /// <summary>
    /// Applies global aggregation and metric tuning rules from the policy.
    /// </summary>
    public void ApplyPolicy(AegisPolicy policy)
    {
        _policy = policy;

        // Example: fine-tuning global weighting or scaling from Performance or Maintainability policies
        _globalHealthWeight = policy.Performance.GlobalHealthWeight > 0 ? policy.Performance.GlobalHealthWeight : 1.0;
        _maintainabilityFactor = policy.Maintainability.Factor > 0 ? policy.Maintainability.Factor : 1.0;
        _resilienceSensitivity = policy.Performance.ResilienceSensitivity > 0 ? policy.Performance.ResilienceSensitivity : 1.0;

        _logger.LogInformation(
            "⚙️ Aggregator policy applied → HealthWeight={Health}, MaintainabilityFactor={Maintain}, ResilienceSensitivity={Resilience}",
            _globalHealthWeight, _maintainabilityFactor, _resilienceSensitivity);
    }

    // ==============================================================
    // 📊 MAIN AGGREGATION LOGIC
    // ==============================================================

    /// <summary>
    /// Builds a complete <see cref="AegisReport"/> from all evaluator results.
    /// </summary>
    public AegisReport Aggregate(IEnumerable<EvaluatorResult> evaluatorResults)
    {
        var report = new AegisReport
        {
            ScanDate = DateTimeOffset.UtcNow,
            Domains = new List<DomainSummary>()
        };

        // 🧩 Flatten all rule results
        var allRules = evaluatorResults.SelectMany(e => e.RuleResults).ToList();
        if (allRules.Count == 0)
        {
            _logger.LogWarning("No rule results were provided for aggregation.");
            report.Metrics.ProjectHealthIndex = 100;
            return report;
        }

        // 🧮 Apply rule weighting
        var weightedResults = _weightingEngine.ApplyWeights(allRules).ToList();

        // 🧱 Group by domain
        var grouped = weightedResults
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Domain) ? "General" : r.Domain)
            .ToList();

        foreach (var domainGroup in grouped)
        {
            var domainResults = domainGroup.ToList();
            var summary = ComputeDomainSummary(domainGroup.Key, domainResults);
            report.Domains.Add(summary);
        }

        // 📊 Compute project-wide metrics
        report.Metrics = ComputeGlobalMetrics(report.Domains);

        _logger.LogInformation(
            "Aggregated {DomainCount} domains ({RuleCount} total rules) into final report.",
            report.Domains.Count,
            allRules.Count);

        return report;
    }

    // ==============================================================
    // 🧩 DOMAIN & PROJECT METRICS
    // ==============================================================

    private DomainSummary ComputeDomainSummary(string domain, List<RuleResult> results)
    {
        var compliant = results.Count(r => r.IsCompliant);
        var total = Math.Max(1, results.Count);

        // Weighted compliance (0–1 scale)
        var weightedCompliance = _weightingEngine.ComputeWeightedCompliance(results);

        // Maintainability = weighted pass ratio × policy factor
        var maintainability = ((double)compliant / total) * _maintainabilityFactor;

        // Health = weighted average of impact scores × global weight
        var health = results.Average(r => r.ImpactScore) * _globalHealthWeight;

        return new DomainSummary
        {
            Domain = domain,
            RulesEvaluated = total,
            Violations = total - compliant,
            WeightedScore = weightedCompliance,
            MaintainabilityIndex = maintainability,
            HealthIndex = health
        };
    }

    private GlobalMetrics ComputeGlobalMetrics(IEnumerable<DomainSummary> domains)
    {
        var list = domains.ToList();
        if (list.Count == 0)
            return new GlobalMetrics { ProjectHealthIndex = 100, WeightedCompliance = 1.0 };

        var resilience = ComputeResilience(list);

        return new GlobalMetrics
        {
            ProjectHealthIndex = Math.Round(list.Average(d => d.HealthIndex * 100), 2),
            MaintainabilityIndex = Math.Round(list.Average(d => d.MaintainabilityIndex * 100), 2),
            WeightedCompliance = Math.Round(list.Average(d => d.WeightedScore), 3),
            ResilienceIndex = Math.Round(resilience, 3)
        };
    }

    private double ComputeResilience(IEnumerable<DomainSummary> domains)
    {
        var scores = domains.Select(d => d.WeightedScore).ToList();
        if (scores.Count <= 1) return 1.0;

        var avg = scores.Average();
        var variance = scores.Average(s => Math.Pow(s - avg, 2));
        var stdDev = Math.Sqrt(variance);

        // Normalize: lower deviation → higher resilience
        var resilience = Math.Clamp(1.0 - (stdDev * _resilienceSensitivity), 0, 1);
        return resilience;
    }
}
