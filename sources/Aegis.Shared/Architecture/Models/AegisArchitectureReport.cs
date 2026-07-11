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
    public void ComputeCompliance()
    {
        var allCategories = ArchitectureRuleRegistry.All
            .Select(r => ParseCategory(r.Category))
            .Distinct()
            .ToList();

        ComplianceScores.Clear();

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

        // Compute domain health averages if available
        if (Domains.Any())
        {
            Metrics.ProjectHealthIndex = Domains.Average(d => d.HealthIndex * 100);
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