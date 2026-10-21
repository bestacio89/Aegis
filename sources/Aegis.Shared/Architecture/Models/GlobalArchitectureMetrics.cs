namespace Aegis.Shared.Architecture.Models;

public sealed class GlobalArchitectureMetrics
{
    public double ProjectHealthIndex { get; set; }
    public double MaintainabilityIndex { get; set; }
    public double ResilienceIndex { get; set; }
    public double WeightedCompliance { get; set; }

    /// <summary>Percentage of applicable registered rules that were evaluated.</summary>
    public double RuleEvaluationCoverage { get; set; }

    /// <summary>Observed health discounted by rule-evaluation coverage; a diagnostic, not a compliance score.</summary>
    public double CoverageAdjustedHealthIndex { get; set; }

    /// <summary>True only when rule coverage is sufficient and no evaluation errors remain.</summary>
    public bool AssessmentComplete { get; set; }
}
