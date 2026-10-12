using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Aegis.Wpf.Models.Security;

namespace Aegis.Wpf.Services.Security;

/// <summary>
/// The ONLY place in the WPF layer that knows the shape of AegisSecurityReport /
/// SecurityComplianceResult. If those models change, adjust this file and nothing else.
///
/// It flattens rule-based results AND heuristic findings into one list, so a domain is never
/// silently empty just because its evaluator emits Findings instead of RuleResults.
/// </summary>
public static class SecurityReportProjector
{
    public static SecurityScanResult Project(AegisSecurityReport report, SecurityComplianceResult compliance)
    {
        var evaluations = report.Summary.DomainSummaries
            .SelectMany(d => d.Evaluations)
            .ToList();

        var findings = new List<SecurityFindingItem>();

        foreach (var eval in evaluations)
        {
            foreach (var r in eval.RuleResults)
            {
                findings.Add(new SecurityFindingItem(
                    r.Severity,
                    eval.Category,
                    r.RuleId,
                    r.Title,
                    r.Target,
                    r.Location,
                    JoinDetail(r.Description, r.Evidence),
                    null,
                    r.Reference));
            }

            foreach (var f in eval.Findings)
            {
                findings.Add(new SecurityFindingItem(
                    f.SeverityLevel,
                    eval.Category,
                    f.Evaluator ?? "heuristic",
                    f.Message,
                    eval.TargetContext,
                    null,
                    f.SourceSignal,
                    f.Remediation,
                    null));
            }
        }

        var complianceByDomain = compliance.DomainResults
            .GroupBy(d => d.Domain)
            .ToDictionary(g => g.Key, g => g.ToList());

        var domainKeys = evaluations.Select(e => e.Category)
            .Concat(findings.Select(f => f.Domain))
            .Concat(compliance.DomainResults.Select(d => d.Domain))
            .Distinct()
            .ToList();

        var domains = new List<SecurityDomainItem>();

        foreach (var domain in domainKeys)
        {
            var own = findings.Where(f => f.Domain == domain).ToList();
            var failed = own.Count(f => f.Severity >= SecuritySeverity.Medium);
            var max = own.Count == 0 ? SecuritySeverity.Info : own.Max(f => f.Severity);

            double rate;
            bool isCompliant;

            if (complianceByDomain.TryGetValue(domain, out var verdicts) && verdicts.Count > 0)
            {
                rate = Math.Round(verdicts.Average(v => v.ComplianceRate), 2);
                isCompliant = verdicts.All(v => v.IsCompliant);
                var verdictMax = verdicts.Max(v => v.MaxSeverity);
                if (verdictMax > max) max = verdictMax;
            }
            else
            {
                rate = own.Count == 0 ? 100.0 : Math.Round((own.Count - failed) * 100.0 / own.Count, 2);
                isCompliant = max < SecuritySeverity.High;
            }

            domains.Add(new SecurityDomainItem(domain, own.Count, failed, rate, max, isCompliant));
        }

        domains = domains
            .OrderBy(d => d.ComplianceRate)
            .ThenBy(d => d.Domain.ToString(), StringComparer.Ordinal)
            .ToList();

        var severityCounts = Enum.GetValues<SecuritySeverity>()
            .Select(s => new SecuritySeverityCount(s, findings.Count(f => f.Severity == s)))
            .ToList();

        return new SecurityScanResult(
            report.ProjectName,
            compliance.GlobalRisk.ToString(),
            compliance.ComplianceRate,
            compliance.IsCompliant,
            compliance.MaxSeverity,
            report.DurationSeconds,
            domains,
            findings,
            severityCounts);
    }

    private static string? JoinDetail(string? description, string? evidence)
    {
        var hasDescription = !string.IsNullOrWhiteSpace(description);
        var hasEvidence = !string.IsNullOrWhiteSpace(evidence);

        if (hasDescription && hasEvidence) return $"{description}\n\nEvidence: {evidence}";
        if (hasDescription) return description;
        return hasEvidence ? $"Evidence: {evidence}" : null;
    }
}