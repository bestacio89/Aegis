using Aegis.Security.Kernel.Governance.Signals;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.Governance;

public sealed class GovernanceDiagnosticsAggregator
{
    public IEnumerable<SecuritySignal> Aggregate(IEnumerable<SecurityEvaluationResult> evals)
    {
        var list = new List<SecuritySignal>();

        int totalFindings = evals.Sum(e => e.RuleResults.Count);
        int critical = evals.Sum(e => e.RuleResults.Count(r => r.Severity == SecuritySeverity.Critical));
        int high = evals.Sum(e => e.RuleResults.Count(r => r.Severity == SecuritySeverity.High));
        int medium = evals.Sum(e => e.RuleResults.Count(r => r.Severity == SecuritySeverity.Medium));

        list.Add(new GovernanceSecuritySignal("TotalFindings", totalFindings.ToString()));
        list.Add(new GovernanceSecuritySignal("CriticalCount", critical.ToString()));
        list.Add(new GovernanceSecuritySignal("HighCount", high.ToString()));
        list.Add(new GovernanceSecuritySignal("MediumCount", medium.ToString()));

        // Rule coverage
        int domains = evals.Select(e => e.Category).Distinct().Count();
        list.Add(new GovernanceSecuritySignal("DomainsCovered", domains.ToString()));

        // Overall max severity
        var maxSev = evals.Max(e => e.MaxSeverity);
        list.Add(new GovernanceSecuritySignal("MaxSeverity", maxSev.ToString()));

        // Average scoring
        double avgScore = evals.Any() ? evals.Average(e => e.AverageScore) : 1.0;
        list.Add(new GovernanceSecuritySignal("AverageScore", avgScore.ToString("F2")));

        return list;
    }
}
