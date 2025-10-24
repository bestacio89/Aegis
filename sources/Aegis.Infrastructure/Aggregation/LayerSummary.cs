using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Infrastructure.Aggregation;

public sealed class LayerSummary
{
    public string LayerName { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int Violations { get; set; }
    public double HealthIndex { get; set; }
    public Dictionary<ArchitectureRuleSeverity, int> SeverityBreakdown { get; set; } = new();
    public List<ArchitectureRuleresult> TopViolations { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();

    public override string ToString() =>
        $"{LayerName}: {HealthIndex:0.00}% Health, {Violations} Violations";
}
