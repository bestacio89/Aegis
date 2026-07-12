using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Represents the aggregated outcome of an entire security scan across multiple domains.
/// </summary>
public sealed class SecurityScanSummary
{
    /// <summary>
    /// Unique identifier for this scan run.
    /// </summary>
    public Guid ScanId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The project or target system this scan applies to.
    /// </summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>
    /// Collection of domain-level summaries (Application, IaC, Network, etc.).
    /// </summary>
    public IReadOnlyCollection<SecurityDomainSummary> DomainSummaries { get; init; }
        = Array.Empty<SecurityDomainSummary>();

    /// <summary>
    /// Total number of findings across all domains.
    /// </summary>
    public int TotalFindings => DomainSummaries.Sum(d => d.TotalFindings);

    /// <summary>
    /// Total number of failed findings (Medium severity or higher).
    /// </summary>
    public int TotalFailed => DomainSummaries.Sum(d => d.FailedFindings);

    /// <summary>
    /// Total number of domains analyzed.
    /// </summary>
    public int DomainCount => DomainSummaries.Count;

    /// <summary>
    /// Global average security score (0–10 scale).
    /// </summary>
    public double GlobalAverageScore => DomainSummaries.Any()
        ? Math.Round(DomainSummaries.Average(d => d.AverageScore), 2)
        : 0.0;

    /// <summary>
    /// Global compliance percentage across all domains.
    /// </summary>
    public double GlobalComplianceRate => DomainSummaries.Any()
        ? Math.Round(DomainSummaries.Average(d => d.ComplianceRate), 2)
        : 100.0;

    /// <summary>
    /// Highest severity found in this scan.
    /// </summary>
    public SecuritySeverity MaxSeverity => DomainSummaries.Any()
        ? DomainSummaries.MaxBy(d => d.MaxSeverity)!.MaxSeverity
        : SecuritySeverity.Info;

    /// <summary>
    /// Aggregated severity distribution across all domains.
    /// </summary>
    public IReadOnlyDictionary<SecuritySeverity, int> SeverityDistribution =>
        DomainSummaries
            .SelectMany(d => d.SeverityDistribution)
            .GroupBy(x => x.Key)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.Value)
            );

    /// <summary>
    /// Derived overall risk level for this scan.
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
    /// Indicates whether this scan found any critical vulnerabilities.
    /// </summary>
    public bool HasCriticalFindings => DomainSummaries.Any(d => d.HasCriticalFindings);

    /// <summary>
    /// The UTC timestamp when the scan was completed.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional version of the security policy used for the scan.
    /// </summary>
    public string PolicyVersion { get; init; } = "v1.0";

    /// <summary>
    /// Optional user or system identifier that executed the scan.
    /// </summary>
    public string ExecutedBy { get; init; } = "system";

    /// <summary>
    /// Compact summary string for console, logs, or dashboards.
    /// </summary>
    public override string ToString() =>
        $"[{ProjectName}] {DomainCount} domains scanned — {TotalFailed}/{TotalFindings} failed | " +
        $"Max={MaxSeverity}, Avg={GlobalAverageScore:F2}, Compliance={GlobalComplianceRate:F2}%";

    // ------------------------------------------------------------------
    // Factory: build from raw evaluation results
    // ------------------------------------------------------------------

    public static SecurityScanSummary FromEvaluations(
        string projectName,
        IEnumerable<SecurityEvaluationResult> evaluations,
        string policyVersion = "v1.0",
        string executedBy = "system")
    {
        var evals = evaluations.ToArray();

        var domainSummaries = evals
            .GroupBy(e => e.Category)
            .Select(g => SecurityDomainSummary.FromEvaluations(g.Key, g))
            .ToArray();

        return new SecurityScanSummary
        {
            ProjectName = projectName,
            DomainSummaries = domainSummaries,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            PolicyVersion = policyVersion,
            ExecutedBy = executedBy
        };
    }
}
