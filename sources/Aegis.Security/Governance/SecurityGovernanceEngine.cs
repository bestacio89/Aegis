using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Aegis.Shared.Security.Models.Compliance;

namespace Aegis.Security.Governance;

/// <summary>
/// Central governance engine responsible for converting the raw multi-domain
/// <see cref="SecurityEvaluationResult"/> outputs into a unified, policy-based
/// compliance verdict (<see cref="SecurityComplianceResult"/>).
/// </summary>
public sealed class SecurityComplianceEngine
{
    /// <summary>
    /// Evaluates all security results and produces a governance-level compliance summary.
    /// Global risk, compliance rate and compliant flag are derived by
    /// <see cref="SecurityComplianceResult"/> from the per-domain results.
    /// </summary>
    public SecurityComplianceResult Evaluate(
        AegisSecurityReport report,
        IEnumerable<SecurityEvaluationResult> domainEvaluations,
        string policyVersion = "v1.0",
        string evaluatedBy = "system")
    {
        var domainResults = domainEvaluations
            .Select(EvaluateDomain)
            .ToList();

        return new SecurityComplianceResult
        {
            ReportId = report.ReportId,
            ProjectName = report.ProjectName,
            PolicyVersion = policyVersion,
            EvaluatedBy = evaluatedBy,
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            DomainResults = domainResults,
        };
    }

    // -----------------------------------------------------------
    // PER-DOMAIN COMPLIANCE EVALUATION
    // -----------------------------------------------------------
    private static DomainComplianceResult EvaluateDomain(SecurityEvaluationResult eval)
    {
        // Rule-driven evaluators populate RuleResults; heuristic evaluators (Cloud, Crypto, OS,
        // TLS, WCF) populate Findings. Both feed compliance, otherwise the heuristic domains come
        // back as a hollow 100% / Info regardless of what they found.
        int total;
        int failed;
        SecuritySeverity maxSeverity;

        if (eval.IsRuleBased)
        {
            var rules = eval.RuleResults;
            total = rules.Count;
            failed = rules.Count(r => r.Severity >= SecuritySeverity.Medium);
            maxSeverity = rules.Max(r => r.Severity);
        }
        else
        {
            var findings = eval.Findings;
            total = findings.Count;
            failed = findings.Count(f => f.SeverityLevel >= SecuritySeverity.Medium);
            maxSeverity = findings.Any() ? findings.Max(f => f.SeverityLevel) : SecuritySeverity.Info;
        }

        // domain compliance rate = percentage of non-broken rules/findings
        double compliance = total == 0
            ? 100.0
            : Math.Round(((double)(total - failed) / total) * 100.0, 2);

        // domain is compliant only if no High/Critical issues exist
        bool isCompliant = maxSeverity < SecuritySeverity.High;

        return new DomainComplianceResult
        {
            Domain = eval.Category,
            EvaluationId = eval.EvaluationId,
            TotalRules = total,
            FailedRules = failed,
            ComplianceRate = compliance,
            MaxSeverity = maxSeverity,
            IsCompliant = isCompliant
        };
    }
}