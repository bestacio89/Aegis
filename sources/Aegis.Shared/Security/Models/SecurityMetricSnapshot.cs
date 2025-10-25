using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Represents a quantitative snapshot of security posture metrics at the time of a scan.
/// </summary>
public sealed class SecurityMetricSnapshot
{
    /// <summary>
    /// Identifier linking this snapshot to a specific report or scan.
    /// </summary>
    public Guid ReportId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Timestamp when the snapshot was generated.
    /// </summary>
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Weighted global risk score (0–10 scale), calculated from severity distribution.
    /// </summary>
    public double GlobalRiskIndex { get; init; }

    /// <summary>
    /// Average security score (0–10), lower means better posture.
    /// </summary>
    public double AverageSeverityScore { get; init; }

    /// <summary>
    /// Percentage of rules passed (0–100).
    /// </summary>
    public double PassRate { get; init; }

    /// <summary>
    /// Total number of findings detected across all domains.
    /// </summary>
    public int TotalFindings { get; init; }

    /// <summary>
    /// Total number of critical or high-severity findings.
    /// </summary>
    public int SevereFindings { get; init; }

    /// <summary>
    /// Derived global risk level based on the highest detected severity.
    /// </summary>
    public RiskLevel RiskLevel { get; init; }

    /// <summary>
    /// Compliance score calculated from (passedRules / totalRules) ratio.
    /// </summary>
    public double ComplianceScore { get; init; }

    /// <summary>
    /// Optional metric showing improvement or regression from previous run (%).
    /// </summary>
    public double TrendDelta { get; init; }

    /// <summary>
    /// Human-readable assessment summary.
    /// </summary>
    public string AssessmentSummary => RiskLevel switch
    {
        RiskLevel.Severe => "Immediate action required — system at high risk.",
        RiskLevel.High => "Significant vulnerabilities detected. Remediation is urgent.",
        RiskLevel.Moderate => "Moderate exposure. Review and mitigate identified risks.",
        RiskLevel.Low => "Generally safe posture with minor issues.",
        RiskLevel.Information => "All checks passed. Excellent posture.",
        _ => "Unknown posture."
    };

    /// <summary>
    /// Returns a concise representation for logs or dashboards.
    /// </summary>
    public override string ToString() =>
        $"[Metrics] Risk={GlobalRiskIndex:F2} | Compliance={ComplianceScore:F2}% | Findings={TotalFindings} | Severe={SevereFindings} | Trend={TrendDelta:+0.00;-0.00}%";
}
