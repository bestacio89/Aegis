using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Aegis.Shared.Security.Models.Compliance;

public sealed class SecurityComplianceResult
{
    public Guid ComplianceId { get; init; }
    public Guid ReportId { get; init; }
    public required string ProjectName { get; init; }
    public required IReadOnlyCollection<DomainComplianceResult> DomainResults { get; init; }
    public SecuritySeverity MaxSeverity { get; }
    public RiskLevel GlobalRisk { get; }
    public double ComplianceRate { get; }
    public bool IsCompliant { get; }
    public DateTimeOffset EvaluatedAtUtc { get; init; }
    public required string PolicyVersion { get; init; }
    public required string EvaluatedBy { get; init; }
}
