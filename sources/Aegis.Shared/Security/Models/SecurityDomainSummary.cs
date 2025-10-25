using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Represents an aggregated summary of all security evaluations for a specific domain (e.g., Application, IaC, Network).
/// </summary>
public sealed class SecurityDomainSummary
{
    /// <summary>
    /// The security domain category represented in this summary.
    /// </summary>
    public SecurityCategory Category { get; init; }

    /// <summary>
    /// Collection of individual evaluation results that belong to this domain.
    /// </summary>
    public IReadOnlyCollection<SecurityEvaluationResult> Evaluations { get; init; } = Array.Empty<SecurityEvaluationResult>();

    /// <summary>
    /// Total number of rule results analyzed under this domain.
    /// </summary>
    public int TotalFindings => Evaluations.Sum(e => e.RuleResults.Count);

    /// <summary>
    /// Total number of failed rules (Medium severity or higher).
    /// </summary>
    public int FailedFindings => Evaluations
        .SelectMany(e => e.RuleResults)
        .Count(r => r.Severity >= SecuritySeverity.Medium);

    /// <summary>
    /// Average score across all rule results in this domain.
    /// </summary>
    public double AverageScore => Evaluations.Any()
        ? Math.Round(Evaluations.SelectMany(e => e.RuleResults).Average(r => r.Score), 2)
        : 0.0;

    /// <summary>
    /// Highest severity found within this domain.
    /// </summary>
    public SecuritySeverity MaxSeverity => Evaluations.Any()
        ? Evaluations.SelectMany(e => e.RuleResults).MaxBy(r => r.Severity)!.Severity
        : SecuritySeverity.Info;

    /// <summary>
    /// Severity distribution histogram.
    /// </summary>
    public IReadOnlyDictionary<SecuritySeverity, int> SeverityDistribution =>
        Evaluations
            .SelectMany(e => e.RuleResults)
            .GroupBy(r => r.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Derived overall risk level for this domain.
    /// </summary>
    public RiskLevel RiskLevel =>
        MaxSeverity switch
        {
            SecuritySeverity.Critical => RiskLevel.Severe,
            SecuritySeverity.High => RiskLevel.High,
            SecuritySeverity.Medium => RiskLevel.Moderate,
            SecuritySeverity.Low => RiskLevel.Low,
            _ => RiskLevel.Information
        };

    /// <summary>
    /// Indicates whether any critical findings exist in this domain.
    /// </summary>
    public bool HasCriticalFindings => Evaluations.Any(e => e.HasCriticalFindings);

    /// <summary>
    /// Percentage of evaluations passing all checks.
    /// </summary>
    public double ComplianceRate
    {
        get
        {
            var total = Evaluations.Count;
            if (total == 0) return 100;
            var compliant = Evaluations.Count(e => e.FailedRules == 0);
            return Math.Round(compliant / (double)total * 100, 2);
        }
    }

    /// <summary>
    /// UTC timestamp of aggregation.
    /// </summary>
    public DateTimeOffset AggregatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Returns a compact summary string for dashboards or reports.
    /// </summary>
    public override string ToString() =>
        $"[{Category}] {FailedFindings} issues ({MaxSeverity}) — AvgScore={AverageScore:F2}, Compliance={ComplianceRate:F2}%";
}
