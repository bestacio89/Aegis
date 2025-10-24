namespace Aegis.Shared.Architecture.Models;

public sealed class ArchitectureDomainSummary
{
    public string Domain { get; set; } = string.Empty;
    public double WeightedScore { get; set; }
    public double MaintainabilityIndex { get; set; }
    public double HealthIndex { get; set; }
    public int Violations { get; set; }
    public int RulesEvaluated { get; set; }
}
