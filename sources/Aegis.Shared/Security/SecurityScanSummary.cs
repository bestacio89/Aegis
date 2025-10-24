namespace Aegis.Shared.Security.Models;

/// <summary>
/// Concise, user-facing scan summary suitable for UI dashboards or CLI output.
/// </summary>
public sealed class SecurityScanSummary
{
    public string Project { get; init; } = string.Empty;
    public DateTimeOffset ScanDate { get; init; } = DateTimeOffset.UtcNow;

    public int TotalFindings { get; init; }
    public int Critical { get; init; }
    public int High { get; init; }
    public int Medium { get; init; }
    public int Low { get; init; }
    public int Info { get; init; }

    public double RiskIndex { get; init; }
}
