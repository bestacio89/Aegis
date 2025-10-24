using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Enums;

namespace Aegis.Infrastructure.Aggregation;

public sealed class LayerSummary
{
    public string LayerName { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int Violations { get; set; }
    public double HealthIndex { get; set; }
    public Dictionary<RuleSeverity, int> SeverityBreakdown { get; set; } = new();
    public List<RuleResult> TopViolations { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();

    public override string ToString() =>
        $"{LayerName}: {HealthIndex:0.00}% Health, {Violations} Violations";
}
