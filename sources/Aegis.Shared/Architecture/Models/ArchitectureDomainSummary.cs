namespace Aegis.Shared.Architecture.Models;

public sealed class ArchitectureDomainSummary
{
    public string Domain { get; set; } = string.Empty;
    public double WeightedScore { get; set; }
    public double MaintainabilityIndex { get; set; }
    public double HealthIndex { get; set; }
    public int Violations { get; set; }

    /// <summary>Distinct rules that were compared against measurements from this domain.</summary>
    public int RulesEvaluated { get; set; }

    /// <summary>Rule-versus-measurement comparisons performed in this domain. 0 = not assessed.</summary>
    public int ChecksPerformed { get; set; }
}