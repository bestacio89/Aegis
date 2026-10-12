using Aegis.Shared.Security.Enums;

namespace Aegis.Wpf.Models.Security;

/// <summary>One finding as shown in the Security findings explorer (rule result or heuristic finding).</summary>
public sealed record SecurityFindingItem(
    SecuritySeverity Severity,
    SecurityCategory Domain,
    string RuleId,
    string Title,
    string Target,
    string? Location,
    string? Detail,
    string? Remediation,
    string? Reference);

/// <summary>Per-domain verdict row (compliance rate is 0-100).</summary>
public sealed record SecurityDomainItem(
    SecurityCategory Domain,
    int Findings,
    int Failed,
    double ComplianceRate,
    SecuritySeverity MaxSeverity,
    bool IsCompliant);

public sealed record SecuritySeverityCount(SecuritySeverity Severity, int Count);

/// <summary>Flat, UI-ready projection of one security scan.</summary>
public sealed record SecurityScanResult(
    string ProjectName,
    string RiskLabel,
    double ComplianceRate,
    bool IsCompliant,
    SecuritySeverity MaxSeverity,
    double DurationSeconds,
    IReadOnlyList<SecurityDomainItem> Domains,
    IReadOnlyList<SecurityFindingItem> Findings,
    IReadOnlyList<SecuritySeverityCount> SeverityCounts);

/// <summary>Shared severity colours (0xRRGGBB) so chart slices and chips always agree.</summary>
public static class SecuritySeverityPalette
{
    public static int Rgb(SecuritySeverity severity) => severity switch
    {
        SecuritySeverity.Critical => 0xE5484D,
        SecuritySeverity.High => 0xE5844D,
        SecuritySeverity.Medium => 0xE8A23D,
        SecuritySeverity.Low => 0x4FA8D8,
        _ => 0x8993A6
    };
}