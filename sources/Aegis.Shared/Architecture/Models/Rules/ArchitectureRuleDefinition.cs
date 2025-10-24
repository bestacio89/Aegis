using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models.Rules
{
    public sealed class ArchitectureRuleDefinition
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string MetricKey { get; init; } = string.Empty;
        public double Threshold { get; set; }
        public ComparisonOperator Operator { get; init; } = ComparisonOperator.LessThan;
        public ArchitectureRuleSeverity Severity { get; init; } = ArchitectureRuleSeverity.Info;
        public string Recommendation { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;

        public bool Evaluate(double value) => Operator switch
        {
            ComparisonOperator.GreaterThan => value > Threshold,
            ComparisonOperator.GreaterOrEqual => value >= Threshold,
            ComparisonOperator.LessThan => value < Threshold,
            ComparisonOperator.LessOrEqual => value <= Threshold,
            _ => false
        };
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
}
