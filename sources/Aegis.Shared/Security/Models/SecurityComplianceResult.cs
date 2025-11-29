using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models.Compliance;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Represents the outcome of applying security policies or baselines
/// to an <see cref="AegisSecurityReport"/>.
/// </summary>
public sealed class SecurityComplianceResult
{
    /// <summary>Unique identifier of this compliance evaluation.</summary>
    public Guid ComplianceId { get; init; } = Guid.NewGuid();

    /// <summary>Reference to the evaluated report ID.</summary>
    public Guid ReportId { get; init; }

    /// <summary>Project or system evaluated.</summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the system passes all enforced compliance thresholds.
    /// </summary>
    public bool IsCompliant => DomainResults.All(d => d.IsCompliant);

    /// <summary>Collection of per-domain compliance verdicts.</summary>
    public IReadOnlyCollection<DomainComplianceResult> DomainResults { get; init; } = Array.Empty<DomainComplianceResult>();

    /// <summary>Highest severity found across all domains.</summary>
    public SecuritySeverity MaxSeverity => DomainResults.Any()
        ? DomainResults.MaxBy(d => d.MaxSeverity)!.MaxSeverity
        : SecuritySeverity.Info;

    /// <summary>Overall derived risk classification.</summary>
    public RiskLevel GlobalRisk => MaxSeverity switch
    {
        SecuritySeverity.Critical => RiskLevel.Severe,
        SecuritySeverity.High => RiskLevel.High,
        SecuritySeverity.Medium => RiskLevel.Moderate,
        SecuritySeverity.Low => RiskLevel.Low,
        _ => RiskLevel.Information
    };

    /// <summary>Overall compliance percentage (0-100%).</summary>
    public double ComplianceRate => DomainResults.Any()
        ? Math.Round(DomainResults.Average(d => d.ComplianceRate), 2)
        : 100.0;

    /// <summary>UTC timestamp when compliance was evaluated.</summary>
    public DateTimeOffset EvaluatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Optional policy or baseline version used for evaluation.</summary>
    public string PolicyVersion { get; init; } = "v1.0";

    /// <summary>Optional assessor or automated agent name.</summary>
    public string EvaluatedBy { get; init; } = "system";

    public override string ToString() =>
        $"[Compliance] {ProjectName} | {(IsCompliant ? "PASS" : "FAIL")} | " +
        $"Risk={GlobalRisk}, Compliance={ComplianceRate:F2}%";
}
