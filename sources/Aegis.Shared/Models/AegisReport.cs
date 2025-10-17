using Aegis.Shared.Enums;
using Aegis.Shared.Models.Rules;
using System.Collections.ObjectModel;

namespace Aegis.Shared.Models;

/// <summary>
/// 🧾 Represents a full Aegis scan result — from evaluator metrics to rule evaluations and aggregated compliance insights.
/// </summary>
public sealed class AegisReport
{
    // 🔖 General metadata
    public string ProjectName { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string? Framework { get; init; } = string.Empty;
    public DateTimeOffset ScanDate { get; init; } = DateTimeOffset.UtcNow;

    // 🧩 Raw results
    public Collection<EvaluatorResult> Facts { get; init; } = new();
    public Collection<RuleResult> Results { get; init; } = new();

    // 📊 Aggregated statistics
    public int TotalFilesScanned { get; set; }
    public int TotalFacts => Facts.Count;
    public int TotalViolations => Results.Count;

    // ⚖️ Compliance Scores by Category (Architecture, Naming, etc.)
    public Dictionary<RuleCategory, double> ComplianceScores { get; init; } = new();

    // 🩺 Global project compliance health index (0–100)
    public double ProjectHealthIndex { get; set; }

    // 📚 Grouped summaries
    public IEnumerable<IGrouping<string, RuleResult>> GroupedByRule()
        => Results.GroupBy(r => r.RuleId);

    public IEnumerable<IGrouping<RuleCategory, RuleResult>> GroupedByCategory()
        => (IEnumerable<IGrouping<RuleCategory, RuleResult>>)Results.GroupBy(r => r.Category);

    // 🧮 Recomputes overall compliance metrics
    public void ComputeCompliance()
    {
        if (Results.Count == 0)
        {
            ProjectHealthIndex = 100;
            return;
        }

        var totalRules = Results.Count;
        var grouped = Results.GroupBy(r => r.Category);

        foreach (var g in grouped)
        {
            var weight = g.Count(r => r.Severity is RuleSeverity.Error or RuleSeverity.Critical) * 2 +
                         g.Count(r => r.Severity == RuleSeverity.Warning);
            var penalty = Math.Clamp(weight * 1.5, 0, 100);
            if (Enum.TryParse<RuleCategory>(g.Key, true, out var category))
            {
                ComplianceScores[category] = Math.Max(0, 100 - penalty);
            }
            else
            {
                // fallback if an unknown category sneaks in
                ComplianceScores[RuleCategory.General] = Math.Max(0, 100 - penalty);
            }
        }

        // Weighted average for global health
        var totalScore = ComplianceScores.Values.DefaultIfEmpty(100).Average();
        ProjectHealthIndex = Math.Round(totalScore, 2);
    }

    // 🧠 Quick summary
    public string Summary =>
        $"🧾 {ProjectName} ({Language}/{Framework}) — {TotalViolations} violations across {TotalFilesScanned} files. HealthIndex: {ProjectHealthIndex:0.##}%";

    // 🗂️ Export-friendly DTO
    public object ToSummaryDto() => new
    {
        Project = ProjectName,
        Language,
        Framework,
        ScanDate,
        Health = ProjectHealthIndex,
        TotalFiles = TotalFilesScanned,
        TotalViolations,
        ComplianceScores = ComplianceScores,
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
