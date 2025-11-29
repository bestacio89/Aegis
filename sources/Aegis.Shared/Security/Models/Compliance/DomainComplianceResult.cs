using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Compliance;

/// <summary>
/// Compliance verdict for a single security domain (Network, OS, Cloud, etc.).
/// </summary>
public sealed class DomainComplianceResult
{
    public Guid EvaluationId { get; init; }

    public SecurityCategory Domain { get; init; }

    public int TotalRules { get; init; }
    public int FailedRules { get; init; }

    /// <summary>
    /// Percentage of rules that passed (0-100%).
    /// </summary>
    public double ComplianceRate { get; init; }

    /// <summary>
    /// Maximum severity of the issues in this domain.
    /// </summary>
    public SecuritySeverity MaxSeverity { get; init; }

    /// <summary>
    /// Indicates if this domain passes compliance thresholds.
    /// </summary>
    public bool IsCompliant { get; init; }

    public override string ToString() =>
        $"{Domain}: {ComplianceRate:F2}% (Compliant={IsCompliant}, Max={MaxSeverity})";
}
