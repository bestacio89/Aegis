using Aegis.Shared.Architecture.Enums;
using System.Collections.Generic;

namespace Aegis.Shared.Architecture.Models.Rules;

/// <summary>
/// Represents the detailed outcome of a single rule evaluation on a specific entity (file, class, or metric).
/// </summary>
public sealed class ArchitectureRuleresult
{
    public ArchitectureRuleresult() { }

    public ArchitectureRuleresult(
        string ruleId,
        string ruleName,
        ArchitectureRuleCategory category,
        ArchitectureRuleSeverity severity,
        string? filePath,
        string? @namespace,
        string message,
        DateTimeOffset detectedAt,
        bool isCompliant = false)
    {
        RuleId = ruleId;
        RuleName = ruleName;
        Category = category;
        Severity = severity;
        FilePath = filePath ?? string.Empty;
        Namespace = @namespace ?? string.Empty;
        Message = message;
        Timestamp = detectedAt;
        IsCompliant = isCompliant;
    }

    // 🧩 Core identification (kept immutable)
    public string RuleId { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public ArchitectureRuleCategory Category { get; set; } = ArchitectureRuleCategory.General;
    public ArchitectureRuleSeverity Severity { get; set; }

    // 📂 Contextual info (settable because analyzers may enrich these)
    public string FilePath { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;          // Architecture / Backend / etc.
    public int? LineNumber { get; set; }                        // Optional for pinpoint diagnostics
    public string? CodeSnippet { get; set; }                    // Optional snippet for reports

    // 🧠 Analytical data (dynamic properties)
    public bool IsCompliant { get; set; }                       // true = rule passed
    public double Weight { get; set; }                          // raw policy weight (1.0 = neutral)
    public double WeightedImpact { get; set; }                  // computed by RuleWeightingEngine
    public double ImpactScore { get; set; }                     // contribution to compliance score

    // 💬 Human-facing data
    public string Message { get; set; } = string.Empty;
    public string? Recommendation { get; set; }                 // AI or policy recommendation
    public List<string> Evidence { get; set; } = new();         // Detected code elements / snippets

    // 🕵️ Traceability (immutable)
    public DateTimeOffset Timestamp { get; init; }              // UTC detection timestamp
    public string DetectedBy { get; init; } = string.Empty;     // Evaluator name / plugin
    public string AnalyzerVersion { get; init; } = "1.0.0";     // Helps when comparing across scans
    public string Target { get; set; } = string.Empty;         // e.g., class name, metric name, etc.

    // 🧾 Derived property for readability
    public string Summary =>
        $"{RuleId} [{Severity}] {(IsCompliant ? "✔️ Pass" : "❌ Fail")} " +
        $"→ {Message} ({Category}/{Domain})";

    // 🧮 Quick compliance computation (optional helper)
    public void ComputeImpact()
    {
        ImpactScore = IsCompliant ? 1.0 : Math.Max(0, 1.0 - WeightedImpact);
    }

    public override string ToString() =>
        $"{RuleId} [{Severity}] {(IsCompliant ? "OK" : "VIOLATION")} in {FilePath}: {Message}";
}
