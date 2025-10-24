namespace Aegis.Shared.Architecture.Models;

public sealed class GlobalArchitectureMetrics
{
    public double ProjectHealthIndex { get; set; }
    public double MaintainabilityIndex { get; set; }
    public double ResilienceIndex { get; set; }
    public double WeightedCompliance { get; set; }
}
