using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models.Rules;

public enum RuleEvaluationScope
{
    Unspecified,
    File,
    Project
}

public sealed class ArchitectureRuleDefinition
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Category { get; init; } = "";

    /// <summary>
    /// Evidence scope required by this rule: per-file or project aggregate.
    /// </summary>
    public RuleEvaluationScope Scope { get; init; }

    public string MetricKey { get; init; } = "";

    public double Threshold { get; set; }

    public ComparisonOperator Operator { get; init; } =
        ComparisonOperator.LessThan;

    public ArchitectureRuleSeverity Severity { get; init; } =
        ArchitectureRuleSeverity.Info;

    public string Recommendation { get; init; } = "";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Returns true when the supplied measurement violates this rule.
    /// Non-finite values and thresholds are never treated as valid evidence.
    /// </summary>
    public bool Evaluate(double value)
    {
        if (!double.IsFinite(value) || !double.IsFinite(Threshold))
        {
            return false;
        }

        return Operator switch
        {
            ComparisonOperator.GreaterThan =>
                value > Threshold,

            ComparisonOperator.GreaterOrEqual =>
                value >= Threshold,

            ComparisonOperator.LessThan =>
                value < Threshold,

            ComparisonOperator.LessOrEqual =>
                value <= Threshold,

            ComparisonOperator.Equal =>
                Math.Abs(value - Threshold) < 0.0001,

            ComparisonOperator.NotEqual =>
                Math.Abs(value - Threshold) >= 0.0001,

            _ => false
        };
    }
}

public enum ComparisonOperator
{
    LessThan,
    LessOrEqual,
    GreaterThan,
    GreaterOrEqual,
    Equal,
    NotEqual
}