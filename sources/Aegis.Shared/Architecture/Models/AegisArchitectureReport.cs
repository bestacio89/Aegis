using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;
using System.Collections.ObjectModel;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// 🧾 Represents a full Aegis scan result — from raw evaluator outputs to
/// aggregated compliance, domain summaries, and health indices.
/// </summary>
public sealed class AegisArchitectureReport
{
    // 🔖 General metadata (must be mutable so AegisAnalyzer can set them post-creation)
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? Framework { get; set; } = string.Empty;
    public DateTimeOffset ScanDate { get; init; } = DateTimeOffset.UtcNow;

    // 🧩 Raw results (remain init-only; collections can still be modified)
    public Collection<ArchitectureEvaluatorResult> Facts { get; init; } = new();
    public Collection<ArchitectureRuleresult> Results { get; init; } = new();

    // 📊 Aggregated domain summaries
    public List<ArchitectureDomainSummary> Domains { get; init; } = new();

    // ⚖️ Global metrics (mutable because it's recomputed)
    public GlobalArchitectureMetrics Metrics { get; set; } = new();

    // 🔎 What the rule engine actually checked (violations alone cannot show that)
    public RuleEvaluationStats? EvaluationStats { get; set; }
    public int RulesRegistered => EvaluationStats?.RulesRegistered ?? 0;
    public int RulesEvaluated => EvaluationStats?.RulesEvaluated ?? 0;
    public int ChecksPerformed => EvaluationStats?.ChecksPerformed ?? 0;

    public int RulesApplicable => EvaluationStats?.RulesApplicable ?? 0;

    public int RulesPassed => EvaluationStats?.RulesPassed ?? 0;

    public int RulesViolated => EvaluationStats?.RulesViolated ?? 0;

    public int EvaluationsPassed => EvaluationStats?.EvaluationsPassed ?? 0;

    public int EvaluationsViolated => EvaluationStats?.EvaluationsViolated ?? 0;

    public int EvaluationsErrored => EvaluationStats?.EvaluationsErrored ?? 0;
    /// <summary>
    /// Diagnostics produced during analysis that may affect report reliability
    /// or rule-evaluation coverage.
    /// </summary>
    public List<AnalysisDiagnostic> Diagnostics { get; init; } = [];

    // 🧮 Derived counts
    public int TotalFilesScanned { get; set; }
    public int TotalFacts => Facts.Count;
    public int TotalViolations => Results.Count(r => !r.IsCompliant);
    /// <summary>
    /// All scoped rule outcomes, including passing evaluations.
    /// Findings remain in Results.
    /// </summary>
    public Collection<RuleEvaluationResult> Evaluations { get; init; } = new();

    // 🧮 Backwards-compatible quick compliance dictionary
    public Dictionary<ArchitectureRuleCategory, double> ComplianceScores { get; set; } = new();

    // 🧠 Recomputes compliance after weighting and aggregation.
    // Covers every category that has at least one registered rule — not just the
    // categories that happen to have a violation — so a clean category is reported
    // as 100% / PASSED instead of being silently absent from the report.
    //
    // When the engine's check statistics are available the caller passes per-category scores
    // (0-100) computed from checks performed, and only categories that were really evaluated are
    // listed: a category with registered rules but no matching measurement is "not evaluated",
    // not "100% compliant". Without them the legacy violation-only estimate is used.
    public void ComputeCompliance(IReadOnlyDictionary<ArchitectureRuleCategory, double>? evaluatedCategoryScores = null)
    {
        ComplianceScores.Clear();

        if (evaluatedCategoryScores is not null)
        {
            foreach (var (category, score) in evaluatedCategoryScores)
                ComplianceScores[category] = score;
        }
        else
        {
            // A findings-only collection has no denominator: absence of findings
            // cannot establish that a rule passed. Rebuild scores only from completed
            // pass/violation evaluations and leave unobserved categories absent.
            var categoryByRuleId = ArchitectureRuleRegistry.All
                .ToDictionary(
                    rule => rule.Id,
                    rule => ParseCategory(rule.Category),
                    StringComparer.OrdinalIgnoreCase);

            var completedEvaluations = Evaluations
                .Where(evaluation =>
                    (evaluation.Status == RuleEvaluationStatus.Passed
                        || evaluation.Status == RuleEvaluationStatus.Violated)
                    && categoryByRuleId.ContainsKey(evaluation.RuleId))
                .ToList();

            foreach (var group in completedEvaluations.GroupBy(
                         evaluation => categoryByRuleId[evaluation.RuleId]))
            {
                var completedCount = group.Count();
                var passedCount = group.Count(
                    evaluation => evaluation.Status == RuleEvaluationStatus.Passed);

                if (completedCount > 0)
                {
                    ComplianceScores[group.Key] = Math.Round(
                        passedCount * 100.0 / completedCount,
                        2);
                }
            }
        }

        // Only domains with completed checks provide health evidence. Default score
        // values on unassessed domains are not measurements and must not inflate health.
        var assessedDomains = Domains
            .Where(domain => domain.ChecksPerformed > 0)
            .ToList();

        if (assessedDomains.Count > 0)
        {
            Metrics.ProjectHealthIndex = Math.Round(
                assessedDomains.Average(domain => domain.HealthIndex * 100),
                2);
            Metrics.HasAssessmentEvidence = true;
        }
        else if (ComplianceScores.Count > 0)
        {
            Metrics.ProjectHealthIndex = Math.Round(
                ComplianceScores.Values.Average(),
                2);
            Metrics.HasAssessmentEvidence = true;
        }
        else
        {
            Metrics.ProjectHealthIndex = 0;
            Metrics.HasAssessmentEvidence = false;
        }

        var stats = EvaluationStats;
        var coverage = stats is not null && stats.RulesApplicable > 0
            ? Math.Clamp(stats.EvaluationCoverage, 0.0, 1.0)
            : 0.0;

        Metrics.RuleEvaluationCoverage = Math.Round(coverage * 100.0, 2);
        Metrics.CoverageAdjustedHealthIndex = Math.Round(
            Metrics.ProjectHealthIndex * coverage,
            2);
        Metrics.AssessmentComplete = Metrics.HasAssessmentEvidence
            && stats is not null
            && stats.RulesApplicable > 0
            && coverage >= 0.95
            && stats.EvaluationsErrored == 0;
    }

    private static ArchitectureRuleCategory ParseCategory(string category) =>
        Enum.TryParse<ArchitectureRuleCategory>(category, true, out var parsed)
            ? parsed
            : ArchitectureRuleCategory.General;

    // 🧾 Quick summary
    public string Summary =>
        $"🧾 {ProjectName} ({Language}/{Framework}) — {TotalViolations} violations across {TotalFilesScanned} files. " +
        $"Assessed HealthIndex: {Metrics.ProjectHealthIndex:0.##}%; " +
        $"RuleCoverage: {Metrics.RuleEvaluationCoverage:0.##}%; " +
        $"AssessmentComplete: {Metrics.AssessmentComplete}.";

    // 🧩 Export-friendly DTO (AI / report writer)
    public object ToSummaryDto() => new
    {
        Project = ProjectName,
        Language,
        Framework,
        ScanDate,
        Health = Metrics.ProjectHealthIndex,
        Metrics,
        Domains,
        TotalFiles = TotalFilesScanned,
        TotalViolations,
        ComplianceScores,
        TopViolations = Results
            .GroupBy(r => r.RuleId)
            .Select(g => new
            {
                Rule = g.Key,
                Severity = g.First().Severity.ToString(),
                Count = g.Count(),
                Category = g.First().Category.ToString()
            })
            .OrderByDescending(x => x.Count)
    };
}