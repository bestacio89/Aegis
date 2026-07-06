using System.Text;
using Aegis.Security.Reporting.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Reporting;

public sealed class MarkdownReportBuilder : IMarkdownReportBuilder
{
    public string Build(AegisSecurityReport report, SecurityComplianceResult compliance)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Aegis Security Report — {report.ProjectName}");
        sb.AppendLine();
        sb.AppendLine($"**Report ID:** `{report.ReportId}`  ");
        sb.AppendLine($"**Engine:** `{report.EngineVersion}`  ");
        sb.AppendLine($"**Policy:** `{report.PolicyVersion}`  ");
        sb.AppendLine($"**Generated:** {report.StartedAtUtc:u} → {report.CompletedAtUtc:u}  ");
        sb.AppendLine($"**Duration:** {report.DurationSeconds:F2}s  ");
        sb.AppendLine();
        sb.AppendLine($"**Global Risk:** **{report.GlobalRisk}**  ");
        sb.AppendLine($"**Max Severity:** **{report.MaxSeverity}**  ");
        sb.AppendLine($"**Compliance:** **{report.ComplianceRate:F2}%**  ");
        sb.AppendLine();

        // Compliance summary
        sb.AppendLine("## Compliance Summary");
        sb.AppendLine();
        sb.AppendLine($"- Overall: **{(compliance.IsCompliant ? "PASS ✅" : "FAIL ❌")}**");
        sb.AppendLine($"- Global Risk: **{compliance.GlobalRisk}**");
        sb.AppendLine($"- Compliance: **{compliance.ComplianceRate:F2}%**");
        sb.AppendLine();

        foreach (var domain in compliance.DomainResults.OrderBy(d => d.Category.ToString()))
        {
            sb.AppendLine($"- **{domain.Category}** → " +
                          $"Risk: **{domain.RiskLevel}**, " +
                          $"Max: **{domain.MaxSeverity}**, " +
                          $"Compliance: **{domain.ComplianceRate:F2}%**");
        }

        sb.AppendLine();
        sb.AppendLine("## Domain Evaluation Details");
        sb.AppendLine();

        foreach (var eval in report.Summary.Evaluations.OrderBy(e => e.Category.ToString()))
        {
            sb.AppendLine($"### {eval.Category}");
            sb.AppendLine();
            sb.AppendLine($"- Max severity: **{eval.MaxSeverity}**");
            sb.AppendLine($"- Avg score: **{eval.AverageScore:F2}**");
            sb.AppendLine($"- Rules: {eval.TotalRules} (Passed: {eval.PassedRules}, Failed: {eval.FailedRules})");
            sb.AppendLine();

            if (!eval.RuleResults.Any())
            {
                sb.AppendLine("_No rule violations detected._");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine("| Severity | Rule | Target | Description |");
            sb.AppendLine("|---------:|------|--------|-------------|");

            foreach (var rr in eval.RuleResults.OrderByDescending(r => r.Severity))
            {
                sb.AppendLine(
                    $"| {rr.Severity} | `{rr.RuleId}` {Escape(rr.Title)} | {Escape(rr.Target)} | {Escape(rr.Description)} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Escape(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("|", "\\|").Replace("\n", " ").Trim();
}
