namespace Aegis.Shared.Models;

public sealed class GlobalMetrics
{
    public double ProjectHealthIndex { get; set; }
    public double MaintainabilityIndex { get; set; }
    public double ResilienceIndex { get; set; }
    public double WeightedCompliance { get; set; }
}
