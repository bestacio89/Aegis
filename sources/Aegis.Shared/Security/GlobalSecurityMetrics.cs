namespace Aegis.Shared.Security.Models;

/// <summary>
/// Rollup metrics across an entire scan or across projects.
/// </summary>
public sealed class GlobalSecurityMetrics
{
    public int TotalFindings { get; init; }
    public int Critical { get; init; }
    public int High { get; init; }
    public int Medium { get; init; }
    public int Low { get; init; }
    public int Info { get; init; }

    /// <summary>
    /// Weighted indicator (e.g., CVSS-inspired) to express overall risk.
    /// Suggested default: Critical*5 + High*3 + Medium*2 + Low*1 normalized by size.
    /// </summary>
    public double RiskIndex { get; init; }

    public static GlobalSecurityMetrics Empty() => new();
}
