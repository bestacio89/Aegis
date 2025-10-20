using Aegis.Shared.Enums;

namespace Aegis.Infrastructure.Aggregation;

public sealed class EvaluatorSummary
{
    public string EvaluatorName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Findings { get; set; }
    public double AverageSeverity { get; set; }
    public List<RuleSeverity> Severities { get; set; } = new();
}
