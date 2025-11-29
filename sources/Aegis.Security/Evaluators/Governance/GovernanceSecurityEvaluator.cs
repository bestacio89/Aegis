using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Evaluators.Governance;

public sealed class GovernanceSecurityEvaluator : ISecurityEvaluator
{
    public string Domain => "Governance";

    public Task<SecurityEvaluationResult> EvaluateAsync(
        ProjectSecurityContext ctx,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default)
    {
        var findings = new List<SecurityFinding>();

        string Get(string key) =>
            signals.OfType<SecuritySignal>()
                .Where(s => s.DetectorId == $"gov:{key}")
                .Select(s => (s as dynamic).Value)
                .FirstOrDefault() ?? "0";

        int critical = int.Parse(Get("CriticalCount"));
        int high = int.Parse(Get("HighCount"));
        double avgScore = double.Parse(Get("AverageScore"));

        // ----- Governance Policies -----

        if (critical > 0)
        {
            findings.Add(new SecurityFinding(
                SecuritySeverity.Critical,
                $"There are {critical} critical vulnerabilities across the system.",
                "Resolve all critical vulnerabilities before deployment.",
                "CriticalCount",
                Domain
            ));
        }

        if (high > 5)
        {
            findings.Add(new SecurityFinding(
                SecuritySeverity.High,
                $"High severity findings exceed threshold: {high}",
                "Reduce high-severity findings to < 5.",
                "HighCount",
                Domain
            ));
        }

        if (avgScore < 5.0)
        {
            findings.Add(new SecurityFinding(
                SecuritySeverity.Medium,
                $"Average security score is low: {avgScore:F2}.",
                "Improve code and infrastructure quality to raise average security score.",
                "AverageScore",
                Domain
            ));
        }

        return Task.FromResult(new SecurityEvaluationResult
        {
            Category = SecurityCategory.Governance,
            Findings = findings
        });
    }
}
