using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Aggregated view of all evaluations for a single security domain
/// (Application, IaC, Network, OS, FileSystem, Cloud, etc.).
/// </summary>
public sealed class SecurityDomainSummary
{
    /// <summary>The logical security domain/category.</summary>
    public SecurityCategory Category { get; init; }
    public DateTimeOffset AggregatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>All evaluation results contributing to this domain.</summary>
 
    /// <summary>
    /// UTC timestamp when this domain summary was aggregated.
    /// </summary>

    public IReadOnlyCollection<SecurityEvaluationResult> Evaluations { get; init; }
        = Array.Empty<SecurityEvaluationResult>();

    /// <summary>Total number of rule results across all evaluations.</summary>
    public int TotalFindings => Evaluations.Sum(e => e.RuleResults.Count);

    /// <summary>
    /// Total number of failed findings (Medium severity or higher).
    /// </summary>
    public int FailedFindings => Evaluations.Sum(e =>
        e.RuleResults.Count(r => r.Severity >= SecuritySeverity.Medium));

    /// <summary>Total number of rules executed across all evaluations.</summary>
    public int TotalRules => Evaluations.Sum(e => e.TotalRules);

    /// <summary>Total number of rules that passed.</summary>
    public int PassedRules => Evaluations.Sum(e => e.PassedRules);

    /// <summary>Average score (0–10) across all evaluations in this domain.</summary>
    public double AverageScore => Evaluations.Any()
        ? Math.Round(Evaluations.Average(e => e.AverageScore), 2)
        : 0.0;

    /// <summary>Compliance percentage for this domain (0–100%).</summary>
    public double ComplianceRate
    {
        get
        {
            var total = TotalRules;
            var failed = FailedFindings;

            if (total == 0)
                return 100.0;

            var compliant = Math.Max(0, total - failed);
            return Math.Round(100.0 * compliant / total, 2);
        }
    }

    /// <summary>Highest severity among all rule results in this domain.</summary>
    public SecuritySeverity MaxSeverity => Evaluations.Any()
        ? Evaluations.MaxBy(e => e.MaxSeverity)!.MaxSeverity
        : SecuritySeverity.Info;

    /// <summary>Aggregated severity distribution for this domain.</summary>
    public IReadOnlyDictionary<SecuritySeverity, int> SeverityDistribution =>
        Evaluations
            .SelectMany(e => e.RuleResults)
            .GroupBy(r => r.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Whether any critical findings exist in this domain.</summary>
    public bool HasCriticalFindings => Evaluations.Any(e => e.HasCriticalFindings);

    public override string ToString() =>
        $"[{Category}] {FailedFindings}/{TotalFindings} failed — Max={MaxSeverity}, Avg={AverageScore:F2}, Compliance={ComplianceRate:F2}%";

    /// <summary>
    /// Convenience factory to build a domain summary from evaluation results.
    /// </summary>
    public static SecurityDomainSummary FromEvaluations(
        SecurityCategory category,
        IEnumerable<SecurityEvaluationResult> evaluations)
    {
        var evals = evaluations.ToArray();
        return new SecurityDomainSummary
        {
            Category = category,
            Evaluations = evals
        };
    }
}
