using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Represents baseline thresholds for compliance evaluation.
/// Typically derived from policy configuration.
/// </summary>
public sealed class ComplianceThreshold
{
    /// <summary>Minimum acceptable compliance rate (0-100%).</summary>
    public double MinComplianceRate { get; init; } = 90.0;

    /// <summary>Maximum allowed severity for any finding.</summary>
    public SecuritySeverity MaxAllowedSeverity { get; init; } = SecuritySeverity.High;

    /// <summary>Minimum acceptable score (0-10 scale).</summary>
    public double MinAverageScore { get; init; } = 2.0;

    public override string ToString() =>
        $"MinCompliance={MinComplianceRate}%, MaxSeverity={MaxAllowedSeverity}, MinScore={MinAverageScore:F1}";
}
