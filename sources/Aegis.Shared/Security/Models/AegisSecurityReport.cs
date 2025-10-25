using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Represents the full report generated after executing a complete Aegis Security scan.
/// </summary>
public sealed class AegisSecurityReport
{
    /// <summary>
    /// Unique identifier of this report instance.
    /// </summary>
    public Guid ReportId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Project or system analyzed.
    /// </summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>
    /// Version of the Aegis Security engine used to produce this report.
    /// </summary>
    public string EngineVersion { get; init; } = "1.0.0";

    /// <summary>
    /// Version of the applied security policy baseline.
    /// </summary>
    public string PolicyVersion { get; init; } = "v1.0";

    /// <summary>
    /// UTC timestamp when the scan started.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp when the scan completed.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Duration in seconds between start and completion.
    /// </summary>
    public double DurationSeconds => (CompletedAtUtc - StartedAtUtc).TotalSeconds;

    /// <summary>
    /// The full aggregated scan summary for this report.
    /// </summary>
    public SecurityScanSummary Summary { get; init; } = new();

    /// <summary>
    /// Optional metric snapshot capturing historical risk and compliance trends.
    /// </summary>
    public SecurityMetricSnapshot? Metrics { get; init; }

    /// <summary>
    /// Optional inference and trace data for causal analysis (rule correlation, propagation, etc.).
    /// </summary>
    public IReadOnlyCollection<SecurityInferenceTrace> InferenceTraces { get; init; } = Array.Empty<SecurityInferenceTrace>();

    /// <summary>
    /// Execution metadata (user, environment, branch, commit SHA, etc.).
    /// </summary>
    public ProjectSecurityMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Indicates whether the report contains critical or severe vulnerabilities.
    /// </summary>
    public bool HasCriticalFindings => Summary.HasCriticalFindings;

    /// <summary>
    /// Derived global risk classification for the entire report.
    /// </summary>
    public RiskLevel GlobalRisk => Summary.RiskLevel;

    /// <summary>
    /// Global compliance rate across all domains.
    /// </summary>
    public double ComplianceRate => Summary.GlobalComplianceRate;

    /// <summary>
    /// Aggregated max severity.
    /// </summary>
    public SecuritySeverity MaxSeverity => Summary.MaxSeverity;

    /// <summary>
    /// Compact string representation for logs or dashboards.
    /// </summary>
    public override string ToString() =>
        $"[Aegis Security Report] {ProjectName} | Risk={GlobalRisk}, Max={MaxSeverity}, Compliance={ComplianceRate:F2}% | Duration={DurationSeconds:F2}s";
}
