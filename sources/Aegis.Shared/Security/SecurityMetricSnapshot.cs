namespace Aegis.Shared.Security.Models;

/// <summary>
/// Snapshot of normalized security metrics at a given time (used for trend charts).
/// </summary>
public sealed class SecurityMetricSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public int TotalFindings { get; init; }
    public int Critical { get; init; }
    public int High { get; init; }
    public int Medium { get; init; }
    public int Low { get; init; }
    public int Info { get; init; }

    /// <summary>Composite index for quick trend comparisons.</summary>
    public double RiskIndex { get; init; }
}
