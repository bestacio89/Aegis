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
    /// </summary>
    public SecurityComplianceResult Evaluate(
        AegisSecurityReport report,
        IEnumerable<SecurityEvaluationResult> domainEvaluations,
        string policyVersion = "v1.0",
        string evaluatedBy = "system")
    {
        var evals = domainEvaluations.ToList();
        var domainResults = new List<DomainComplianceResult>();

        foreach (var eval in evals)
        {
            domainResults.Add(EvaluateDomain(eval));
        }

        // ------- Aggregate GLOBAL compliance -------

        var overallMaxSeverity = domainResults.Any()
            ? domainResults.Max(r => r.MaxSeverity)
            : SecuritySeverity.Info;

        var globalRisk = overallMaxSeverity switch
        {
            SecuritySeverity.Critical => RiskLevel.Severe,
            SecuritySeverity.High => RiskLevel.High,
            SecuritySeverity.Medium => RiskLevel.Moderate,
            SecuritySeverity.Low => RiskLevel.Low,
            _ => RiskLevel.Information
        };

        var complianceRate = domainResults.Any()
            ? Math.Round(domainResults.Average(r => r.ComplianceRate), 2)
            : 100.0;

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
    private DomainComplianceResult EvaluateDomain(SecurityEvaluationResult eval)
    {
        var rules = eval.RuleResults.ToList();

        int total = rules.Count;
        int failed = rules.Count(r => r.Severity != SecuritySeverity.Info && r.Severity != SecuritySeverity.Low);

        // domain compliance rate = percentage of non-broken rules
        double compliance = total == 0
            ? 100.0
            : Math.Round(((double)(total - failed) / total) * 100.0, 2);

        var maxSeverity = rules.Any()
            ? rules.Max(r => r.Severity)
            : SecuritySeverity.Info;

        // domain is compliant only if no High/Critical issues exist
        bool isCompliant = maxSeverity switch
        {
            SecuritySeverity.Critical => false,
            SecuritySeverity.High => false,
            _ => true
        };

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
