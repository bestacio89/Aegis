using System.Text;
using Aegis.Security.Reporting.Contracts;
using Aegis.Shared.Security.Models;
using Aegis.Shared.Security.Enums;
using System.Linq;

namespace Aegis.Security.Reporting;

public sealed class MarkdownReportBuilder : IMarkdownReportBuilder
{
    public string Build(AegisSecurityReport report)
    {
        var sb = new StringBuilder();

        WriteHeader(sb, report);
        WriteScanSummary(sb, report.Summary);
        WriteDomains(sb, report.Summary.DomainSummaries);

        return sb.ToString();
    }

    private static void WriteHeader(StringBuilder sb, AegisSecurityReport report)
    {
        sb.AppendLine($"# Aegis Security Report — {report.ProjectName}");
        sb.AppendLine();

        sb.AppendLine($"**Report ID:** `{report.ReportId}`");
        sb.AppendLine($"**Engine:** `{report.EngineVersion}`");
        sb.AppendLine($"**Policy:** `{report.PolicyVersion}`");
        sb.AppendLine($"**Executed By:** `{report.Metadata?.ExecutedBy}`");
        sb.AppendLine($"**Generated:** {report.StartedAtUtc:u} → {report.CompletedAtUtc:u}");
        sb.AppendLine($"**Duration:** {report.DurationSeconds:F2}s");
        sb.AppendLine();

        sb.AppendLine($"**Global Risk:** **{report.GlobalRisk}**");
        sb.AppendLine($"**Max Severity:** **{report.MaxSeverity}**");
        sb.AppendLine($"**Compliance:** **{report.ComplianceRate:F2}%**");
        sb.AppendLine();
    }

    private static void WriteScanSummary(StringBuilder sb, SecurityScanSummary summary)
    {
        sb.AppendLine("## Scan Summary");
        sb.AppendLine();

        sb.AppendLine($"- Domains: {summary.DomainCount}");
        sb.AppendLine($"- Total Findings: {summary.TotalFindings}");
        sb.AppendLine($"- Failed Findings: {summary.TotalFailed}");
        sb.AppendLine($"- Average Score: {summary.GlobalAverageScore:F2}");
        sb.AppendLine($"- Compliance: {summary.GlobalComplianceRate:F2}%");
        sb.AppendLine($"- Risk Level: **{summary.RiskLevel}**");
        sb.AppendLine();
    }

    private static void WriteDomains(StringBuilder sb, IReadOnlyCollection<SecurityDomainSummary> domains)
    {
        sb.AppendLine("## Domains");
        sb.AppendLine();

        foreach (var domain in domains.OrderBy(d => d.Category))
        {
            sb.AppendLine($"### {domain.Category}");
            sb.AppendLine();

            sb.AppendLine($"- Evaluations: {domain.Evaluations.Count}");
            sb.AppendLine($"- Rules: {domain.TotalRules} (Passed: {domain.PassedRules}, Failed: {domain.FailedFindings})");
            sb.AppendLine($"- Avg Score: {domain.AverageScore:F2}");
            sb.AppendLine($"- Compliance: {domain.ComplianceRate:F2}%");
            sb.AppendLine($"- Max Severity: **{domain.MaxSeverity}**");
            sb.AppendLine();

            WriteEvaluations(sb, domain.Evaluations);
        }
    }

    private static void WriteEvaluations(StringBuilder sb, IReadOnlyCollection<SecurityEvaluationResult> evals)
    {
        foreach (var eval in evals.OrderBy(e => e.TargetContext))
        {
            sb.AppendLine($"#### Evaluation: {eval.TargetContext}");
            sb.AppendLine();

            sb.AppendLine($"- Risk: **{eval.RiskLevel}**");
            sb.AppendLine($"- Max Severity: **{eval.MaxSeverity}**");
            sb.AppendLine($"- Avg Score: {eval.AverageScore:F2}");
            sb.AppendLine($"- Rules: {eval.TotalRules} (Passed: {eval.PassedRules}, Failed: {eval.FailedRules})");
            sb.AppendLine();

            if (eval.RuleResults.Any())
            {
                sb.AppendLine("| Severity | Rule | Score | Target | Description |");
                sb.AppendLine("|---------:|------|------:|--------|-------------|");

                foreach (var r in eval.RuleResults.OrderByDescending(r => r.Severity))
                {
                    sb.AppendLine(
                        $"| {r.Severity} | `{r.RuleId}` | {r.Score:F2} | {r.Target} | {Escape(r.Description)} |"
                    );
                }

                sb.AppendLine();
            }

            if (eval.Findings.Any())
            {
                sb.AppendLine("**Findings**");
                sb.AppendLine();

                foreach (var f in eval.Findings.OrderByDescending(f => f.SeverityLevel))
                {
                    sb.AppendLine(
                        $"- **{f.SeverityLevel}** {Escape(f.Message)}"
                    );
                }

                sb.AppendLine();
            }
        }
    }

    private static string Escape(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("|", "\\|").Replace("\n", " ").Trim();
}