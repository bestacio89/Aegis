using Aegis.Architecture.Scoring;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Aggregation;

/// <summary>
/// Aggregates RuleEngine outputs into deterministic architecture health metrics.
///
/// Evaluators produce facts.
/// RuleEngine produces violations and the statistics of what it checked.
/// Aggregator only combines them.
///
/// Compliance is the share of weighted CHECKS that passed
/// (1 - weighted violations / weighted checks), per domain and per category.
/// Violations alone are not enough: a run with no violations and no checks is "not assessed",
/// not "100% compliant", and a run with violations must still count everything that passed.
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
    /// <paramref name="stats"/> carries what the rule engine actually checked; callers without it
    /// get the previous violation-only estimate.
    /// </summary>
    public AegisArchitectureReport Aggregate(
        IEnumerable<ArchitectureRuleresult> ruleResults,
        IEnumerable<ArchitectureEvaluatorResult>? facts = null,
        ProjectArchitectureContext? context = null,
        RuleEvaluationStats? stats = null)
    {
        var rules =
            ruleResults
                .ToList();



        var report =
            new AegisArchitectureReport
            {
                ScanDate = DateTimeOffset.UtcNow,
                EvaluationStats = stats
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



        var weighted =
            _weightingEngine
                .ApplyWeights(rules)
                .ToList();



        // Every known layer, every layer with violations and every layer where checks ran gets an
        // entry, so a fully compliant layer appears as "evaluated and clean" instead of being
        // silently absent from report.Domains.
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


        var domainNames =
            GetKnownLayerNames(
                context);

        var additionalDomains =
            groupedByLayer.Keys
                .Concat(
                    stats?.CheckBuckets.Select(b => b.Domain)
                    ?? Enumerable.Empty<string>());

        foreach (var name in additionalDomains)
        {
            if (!domainNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                domainNames.Add(name);
            }
        }


        foreach (var domainName in domainNames)
        {
            var rulesForDomain =
                groupedByLayer.TryGetValue(domainName, out var list)
                    ? list
                    : new List<ArchitectureRuleresult>();

            report.Domains.Add(
                ComputeDomainSummary(
                    domainName,
                    rulesForDomain,
                    stats));
        }



        report.Metrics =
            ComputeGlobalMetrics(
                report.Domains,
                stats);



        report.ComputeCompliance(
            ComputeCategoryScores(
                weighted,
                stats));



        _logger.LogInformation(
            "Aggregated {Rules} violations into {Domains} domains.",
            weighted.Count,
            report.Domains.Count);



        return report;
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
        List<ArchitectureRuleresult> rules,
        RuleEvaluationStats? stats)
    {
        if (stats is null)
        {
            return ComputeLegacyDomainSummary(
                domain,
                rules);
        }


        var violations =
            rules.Count(x => !x.IsCompliant);


        var buckets =
            stats.CheckBuckets
                .Where(b => string.Equals(
                    b.Domain,
                    domain,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();


        var checks =
            buckets.Sum(b => b.Checks);


        var checkWeight =
            buckets.Sum(b =>
                b.Checks * _weightingEngine.WeightOf(b.Severity, b.Category));


        var violationWeight =
            rules.Sum(x => x.WeightedImpact);


        // No checks in this domain = not assessed (reported through ChecksPerformed = 0).
        var score =
            checks == 0
                ? 0.0
                : _weightingEngine.ComputeScore(violationWeight, checkWeight);


        var passRate =
            checks == 0
                ? 0.0
                : Math.Clamp(1.0 - (double)violations / checks, 0, 1);


        return new ArchitectureDomainSummary
        {
            Domain = domain,

            RulesEvaluated =
                stats.RulesEvaluatedByDomain.GetValueOrDefault(domain),

            ChecksPerformed = checks,

            HasAssessmentEvidence = checks > 0,

            Violations = violations,

            WeightedScore = score,

            MaintainabilityIndex = passRate * _maintainabilityFactor,

            HealthIndex = Math.Clamp(score * _globalHealthWeight, 0, 1)
        };
    }



    // Previous behaviour, kept for callers that cannot supply check statistics.
    private ArchitectureDomainSummary ComputeLegacyDomainSummary(
        string domain,
        List<ArchitectureRuleresult> rules)
    {
        if (rules.Count == 0)
        {
            return new ArchitectureDomainSummary
            {
                Domain = domain,
                RulesEvaluated = 0,
                ChecksPerformed = 0,
                HasAssessmentEvidence = false,
                Violations = 0,
                WeightedScore = 0,
                MaintainabilityIndex = 0,
                HealthIndex = 0
            };
        }


        // Legacy callers supply findings without the corresponding passing checks.
        // Findings are useful evidence of problems, but they do not provide a denominator
        // from which a compliance or health score can be calculated.
        return new ArchitectureDomainSummary
        {
            Domain = domain,
            RulesEvaluated = 0,
            ChecksPerformed = 0,
            HasAssessmentEvidence = false,
            Violations = rules.Count,
            WeightedScore = 0,
            MaintainabilityIndex = 0,
            HealthIndex = 0
        };
    }



    /// <summary>
    /// Per-category compliance (0-100) from checks performed. Only categories that were really
    /// evaluated are returned. Null when no statistics are available.
    /// </summary>
    private Dictionary<ArchitectureRuleCategory, double>? ComputeCategoryScores(
        List<ArchitectureRuleresult> weighted,
        RuleEvaluationStats? stats)
    {
        if (stats is null)
        {
            return null;
        }


        var checkWeightByCategory =
            new Dictionary<ArchitectureRuleCategory, double>();

        foreach (var bucket in stats.CheckBuckets)
        {
            var category = ParseCategory(bucket.Category);

            checkWeightByCategory[category] =
                checkWeightByCategory.GetValueOrDefault(category)
                + bucket.Checks * _weightingEngine.WeightOf(bucket.Severity, bucket.Category);
        }


        var violationWeightByCategory =
            weighted
                .Where(r => !r.IsCompliant)
                .GroupBy(r => r.Category)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(r => r.WeightedImpact));


        var scores =
            new Dictionary<ArchitectureRuleCategory, double>();

        foreach (var (category, checkWeight) in checkWeightByCategory)
        {
            var score =
                _weightingEngine.ComputeScore(
                    violationWeightByCategory.GetValueOrDefault(category),
                    checkWeight);

            scores[category] = Math.Round(score * 100.0, 2);
        }


        return scores;
    }



    private static ArchitectureRuleCategory ParseCategory(string category) =>
        Enum.TryParse<ArchitectureRuleCategory>(category, true, out var parsed)
            ? parsed
            : ArchitectureRuleCategory.General;



    private GlobalArchitectureMetrics ComputeGlobalMetrics(
        IEnumerable<ArchitectureDomainSummary> domains,
        RuleEvaluationStats? stats)
    {
        var list =
            domains.ToList();



        if (list.Count == 0)
        {
            return CreateUnassessedGlobalMetrics(stats);
        }


        // An unassessed domain is not a clean domain. If no valid comparisons ran,
        // return an explicitly unassessed score instead of averaging default 1.0 values.
        var assessed =
            list
                .Where(x => x.ChecksPerformed > 0)
                .ToList();

        if (assessed.Count == 0)
        {
            return CreateUnassessedGlobalMetrics(stats);
        }



        var totalChecks = assessed.Sum(domain => (double)domain.ChecksPerformed);

        return new GlobalArchitectureMetrics
        {
            // Weight aggregate scores by the number of completed checks in each domain.
            // A tiny domain must not have the same influence as a domain with many checks.
            ProjectHealthIndex = Math.Round(
                assessed.Sum(domain => domain.HealthIndex * domain.ChecksPerformed)
                    / totalChecks * 100.0,
                2),

            MaintainabilityIndex = Math.Round(
                assessed.Sum(domain => domain.MaintainabilityIndex * domain.ChecksPerformed)
                    / totalChecks * 100.0,
                2),

            WeightedCompliance = Math.Round(
                assessed.Sum(domain => domain.WeightedScore * domain.ChecksPerformed)
                    / totalChecks,
                3),

            ResilienceIndex = Math.Round(
                ComputeResilience(assessed),
                3),

            HasAssessmentEvidence = true,
            RuleEvaluationCoverage = stats?.EvaluationCoverage ?? 0,
            HasCoverageEvidence = stats is not null
                && stats.RulesApplicable + stats.RulesUnsupported > 0,
            CoverageAdjustedHealthIndex = Math.Round(
                assessed.Sum(domain => domain.HealthIndex * domain.ChecksPerformed)
                    / totalChecks * 100.0
                    * (stats?.EvaluationCoverage ?? 0),
                2),
            AssessmentComplete = stats is not null
                && stats.RulesUnsupported == 0
                && stats.RulesNotEvaluated.Count == 0
                && stats.RulesWithErrors == 0
                && stats.EvaluationsInsufficientEvidence == 0
                && stats.EvaluationsErrored == 0
        };
    }

    private static GlobalArchitectureMetrics CreateUnassessedGlobalMetrics(
        RuleEvaluationStats? stats) =>
        new()
        {
            ProjectHealthIndex = 0,
            MaintainabilityIndex = 0,
            ResilienceIndex = 0,
            WeightedCompliance = 0,
            HasAssessmentEvidence = false,
            RuleEvaluationCoverage = stats?.EvaluationCoverage ?? 0,
            HasCoverageEvidence = stats is not null
                && stats.RulesApplicable + stats.RulesUnsupported > 0,
            CoverageAdjustedHealthIndex = 0,
            AssessmentComplete = stats is not null
                && stats.RulesApplicable + stats.RulesUnsupported > 0
                && stats.RulesNotEvaluated.Count == 0
                && stats.RulesUnsupported == 0
                && stats.RulesWithErrors == 0
                && stats.EvaluationsInsufficientEvidence == 0
                && stats.EvaluationsErrored == 0
        };



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