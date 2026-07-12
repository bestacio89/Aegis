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
            PopulateAllLayersAsClean(
                report,
                context);

            report.Metrics.ProjectHealthIndex = 100;

            report.ComputeCompliance();

            return report;
        }



        var weighted =
            _weightingEngine
                .ApplyWeights(rules)
                .ToList();



        // Left-join every known layer (from context.Modules) against the actual violation
        // groups, so a fully compliant layer (e.g. Domain with zero findings) still gets a
        // 0-violation / 100-health entry instead of being silently absent from report.Domains
        // — absence previously read as "not evaluated" rather than "evaluated and clean."
        var groupedByLayer =
            weighted
                .GroupBy(x =>
                    string.IsNullOrWhiteSpace(x.Domain)
                        ? "Unclassified"
                        : x.Domain)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToList(),
                    StringComparer.OrdinalIgnoreCase);


        var knownLayers =
            GetKnownLayerNames(
                context);


        foreach (var layerName in knownLayers)
        {
            var rulesForLayer =
                groupedByLayer.TryGetValue(layerName, out var list)
                    ? list
                    : new List<ArchitectureRuleresult>();

            report.Domains.Add(
                ComputeDomainSummary(
                    layerName,
                    rulesForLayer));
        }


        // Any violation whose layer wasn't in the known-layers list (e.g. genuinely
        // "Unclassified" files, or a module the layer detector didn't recognize) still needs
        // to be represented rather than silently dropped.
        foreach (var kvp in groupedByLayer)
        {
            if (!knownLayers.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                report.Domains.Add(
                    ComputeDomainSummary(
                        kvp.Key,
                        kvp.Value));
            }
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



    /// <summary>
    /// Populates report.Domains with every known layer at full health when there are zero
    /// violations project-wide — without this, a perfectly clean scan would leave Domains
    /// empty instead of showing every layer as compliant.
    /// </summary>
    private void PopulateAllLayersAsClean(
        AegisArchitectureReport report,
        ProjectArchitectureContext? context)
    {
        foreach (var layerName in GetKnownLayerNames(context))
        {
            report.Domains.Add(
                ComputeDomainSummary(
                    layerName,
                    new List<ArchitectureRuleresult>()));
        }
    }



    private static List<string> GetKnownLayerNames(
        ProjectArchitectureContext? context)
    {
        if (context is null)
        {
            return new List<string>();
        }


        return context.Modules
            .SelectMany(m => m.Layers)
            .Select(l => l.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }



    private ArchitectureDomainSummary ComputeDomainSummary(
        string domain,
        List<ArchitectureRuleresult> rules)
    {
        // An empty rule list means "this layer was evaluated and found clean" — not
        // "1 rule evaluated, 1 violation," which is what Math.Max(1, rules.Count) previously
        // produced for every genuinely clean layer once full-layer enumeration was added.
        if (rules.Count == 0)
        {
            return new ArchitectureDomainSummary
            {
                Domain = domain,

                RulesEvaluated = 0,

                Violations = 0,

                WeightedScore = 1,

                MaintainabilityIndex = 1 * _maintainabilityFactor,

                HealthIndex = 1 * _globalHealthWeight
            };
        }



        var total =
            rules.Count;



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
                rules.Average(
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