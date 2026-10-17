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

    // 🧮 Derived counts
    public int TotalFilesScanned { get; set; }
    public int TotalFacts => Facts.Count;
    public int TotalViolations => Results.Count(r => !r.IsCompliant);

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
            var allCategories = ArchitectureRuleRegistry.All
                .Select(r => ParseCategory(r.Category))
                .Distinct()
                .ToList();

            var grouped = Results
                .GroupBy(r => r.Category)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var category in allCategories)
            {
                if (grouped.TryGetValue(category, out var categoryResults))
                {
                    var severityPenalty = categoryResults.Sum(r =>
                        r.IsCompliant ? 0 : ((int)r.Severity + 1) * 10);

                    var score = Math.Max(0, 100 - severityPenalty / Math.Max(1, categoryResults.Count));
                    ComplianceScores[category] = score;
                }
                else
                {
                    // Rules exist for this category and none of them fired — genuinely clean.
                    ComplianceScores[category] = 100;
                }
            }
        }

        // Compute domain health averages if available
        if (Domains.Any())
        {
            // Domains in which nothing was checked must not dilute the health of the ones that were.
            var assessed = Domains.Where(d => d.ChecksPerformed > 0).ToList();
            var basis = assessed.Count > 0 ? assessed : Domains;

            Metrics.ProjectHealthIndex = basis.Average(d => d.HealthIndex * 100);
        }
        else
        {
            Metrics.ProjectHealthIndex = ComplianceScores.Values.DefaultIfEmpty(100).Average();
        }

        Metrics.ProjectHealthIndex = Math.Round(Metrics.ProjectHealthIndex, 2);
    }

    private static ArchitectureRuleCategory ParseCategory(string category) =>
        Enum.TryParse<ArchitectureRuleCategory>(category, true, out var parsed)
            ? parsed
            : ArchitectureRuleCategory.General;

    // 🧾 Quick summary
    public string Summary =>
        $"🧾 {ProjectName} ({Language}/{Framework}) — {TotalViolations} violations across {TotalFilesScanned} files. HealthIndex: {Metrics.ProjectHealthIndex:0.##}%";

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