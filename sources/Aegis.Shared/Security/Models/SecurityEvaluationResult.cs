using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Aegis.Shared.Security.Models.Rules;

public sealed class SecurityEvaluationResult
{
    public Guid EvaluationId { get; init; } = Guid.NewGuid();
    public string TargetContext { get; init; } = string.Empty;

    public SecurityCategory Category { get; init; }

    public IReadOnlyCollection<SecurityRuleResult> RuleResults { get; init; }
        = Array.Empty<SecurityRuleResult>();

    public IReadOnlyCollection<SecurityFinding> Findings { get; init; }
        = Array.Empty<SecurityFinding>();

    public bool IsRuleBased => RuleResults.Count > 0;

    public double AverageScore =>
        IsRuleBased
            ? Math.Round(RuleResults.Average(r => r.Score), 2)
            : Findings.Count == 0 ? 1.0 : 0.5;

    public SecuritySeverity MaxSeverity =>
        IsRuleBased
            ? RuleResults.MaxBy(r => r.Severity)!.Severity
            : (Findings.Count == 0
                ? SecuritySeverity.Info
                : Findings.MaxBy(f => f.SeverityLevel)!.SeverityLevel);

    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
