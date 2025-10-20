using Aegis.Shared.Enums;

namespace Aegis.Infrastructure.Aggregation;

public sealed class CategorySummary
{
    public string CategoryName { get; set; } = string.Empty;
    public int TotalViolations { get; set; }
    public double HealthIndex { get; set; }
    public Dictionary<RuleSeverity, int> SeverityBreakdown { get; set; } = new();
    public Dictionary<string, LayerSummary> Layers { get; set; } = new();

    public override string ToString() =>
        $"{CategoryName} → {HealthIndex:0.0}% ({TotalViolations} violations)";
}
