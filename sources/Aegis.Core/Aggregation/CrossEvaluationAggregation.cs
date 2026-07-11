using Aegis.Architecture.Scoring;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Aggregation;

/// <summary>
/// Aggregates RuleEngine outputs into deterministic architecture health metrics.
///
/// Evaluators produce facts.
/// RuleEngine produces violations.
/// Aggregator only combines rule results.
/// </summary>
public sealed class CrossEvaluatorAggregator
{
    private readonly ILogger<CrossEvaluatorAggregator> _logger;

    private readonly RuleWeightingEngine _weightingEngine;



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



    public void ApplyPolicy(
        AegisArchitecturePolicy policy)
    {
        _globalHealthWeight =
            policy.Performance.GlobalHealthWeight > 0
                ? policy.Performance.GlobalHealthWeight
                : 1.0;


        _maintainabilityFactor =
            policy.Maintainability.Factor > 0
                ? policy.Maintainability.Factor
                : 1.0;


        _resilienceSensitivity =
            policy.Performance.ResilienceSensitivity > 0
                ? policy.Performance.ResilienceSensitivity
                : 1.0;



        _logger.LogInformation(
            "Aggregator policy applied. Health={Health}, Maintainability={Maintainability}, Resilience={Resilience}",
            _globalHealthWeight,
            _maintainabilityFactor,
            _resilienceSensitivity);
    }



    /// <summary>
    /// Builds final architecture report from RuleEngine results.
    /// </summary>
    public AegisArchitectureReport Aggregate(
        IEnumerable<ArchitectureRuleresult> ruleResults,
        IEnumerable<ArchitectureEvaluatorResult>? facts = null,
        ProjectArchitectureContext? context = null)
    {
        var rules =
            ruleResults
                .ToList();



        var report =
            new AegisArchitectureReport
            {
                ScanDate = DateTimeOffset.UtcNow
            };



        if (context is not null)
        {
            report.ProjectName =
                context.ProjectName;

            report.ProjectPath =
                context.RootPath;

            report.Language =
                context.Language;

            report.Framework =
                context.Framework;

            report.TotalFilesScanned =
                context.FileCount;
        }



        if (facts is not null)
        {
            foreach (var fact in facts)
            {
                report.Facts.Add(fact);
            }
        }



        foreach (var rule in rules)
        {
            report.Results.Add(rule);
        }



        if (rules.Count == 0)
        {
            report.Metrics.ProjectHealthIndex = 100;

            report.ComputeCompliance();

            return report;
        }



        var weighted =
            _weightingEngine
                .ApplyWeights(rules)
                .ToList();



        foreach (var group in weighted.GroupBy(
                     x => string.IsNullOrWhiteSpace(x.Domain)
                         ? "General"
                         : x.Domain))
        {
            report.Domains.Add(
                ComputeDomainSummary(
                    group.Key,
                    group.ToList()));
        }



        report.Metrics =
            ComputeGlobalMetrics(
                report.Domains);



        report.ComputeCompliance();



        _logger.LogInformation(
            "Aggregated {Rules} rules into {Domains} domains.",
            weighted.Count,
            report.Domains.Count);



        return report;
    }



    private ArchitectureDomainSummary ComputeDomainSummary(
        string domain,
        List<ArchitectureRuleresult> rules)
    {
        var total =
            Math.Max(
                1,
                rules.Count);



        var compliant =
            rules.Count(
                x => x.IsCompliant);



        return new ArchitectureDomainSummary
        {
            Domain = domain,

            RulesEvaluated = total,

            Violations =
                total - compliant,


            WeightedScore =
                _weightingEngine
                    .ComputeWeightedCompliance(rules),


            MaintainabilityIndex =
                ((double)compliant / total)
                *
                _maintainabilityFactor,


            HealthIndex =
                rules.Count == 0
                    ? 1
                    : rules.Average(
                        x => x.ImpactScore)
                    *
                    _globalHealthWeight
        };
    }



    private GlobalArchitectureMetrics ComputeGlobalMetrics(
        IEnumerable<ArchitectureDomainSummary> domains)
    {
        var list =
            domains.ToList();



        if (list.Count == 0)
        {
            return new GlobalArchitectureMetrics
            {
                ProjectHealthIndex = 100,

                WeightedCompliance = 1
            };
        }



        return new GlobalArchitectureMetrics
        {
            ProjectHealthIndex =
                Math.Round(
                    list.Average(
                        x => x.HealthIndex * 100),
                    2),


            MaintainabilityIndex =
                Math.Round(
                    list.Average(
                        x => x.MaintainabilityIndex * 100),
                    2),


            WeightedCompliance =
                Math.Round(
                    list.Average(
                        x => x.WeightedScore),
                    3),


            ResilienceIndex =
                Math.Round(
                    ComputeResilience(list),
                    3)
        };
    }



    private double ComputeResilience(
        IEnumerable<ArchitectureDomainSummary> domains)
    {
        var scores =
            domains
                .Select(x => x.WeightedScore)
                .ToList();


        if (scores.Count <= 1)
            return 1;



        var average =
            scores.Average();


        var variance =
            scores.Average(
                x => Math.Pow(
                    x - average,
                    2));


        var deviation =
            Math.Sqrt(
                variance);



        return Math.Clamp(
            1 - deviation * _resilienceSensitivity,
            0,
            1);
    }
}