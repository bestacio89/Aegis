namespace Aegis.Shared.Architecture.Models.Rules;

/// <summary>
/// An auditable outcome for one rule applied to one evidence scope.
/// This is not a finding: a passing evaluation is a valid result.
/// </summary>
public sealed class RuleEvaluationResult
{
    public string RuleId { get; init; } = string.Empty;

    public string RuleName { get; init; } = string.Empty;

    public string MetricKey { get; init; } = string.Empty;

    public RuleEvaluationStatus Status { get; init; }

    /// <summary>Project, file, type, method, or another defined scope.</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>Identifier of the target evaluated at this scope.</summary>
    public string Target { get; init; } = string.Empty;

    public string Domain { get; init; } = string.Empty;

    public double? ObservedValue { get; init; }

    public double? Threshold { get; init; }

    public string ComparisonOperator { get; init; } = string.Empty;

    /// <summary>
    /// Stable diagnostic code, such as MissingMetric,
    /// InvalidThreshold, or InvalidMetric.
    /// </summary>
    public string? ReasonCode { get; init; }

    public string? Reason { get; init; }

    public string? Evaluator { get; init; }

    /// <summary>
    /// References findings produced by this evaluation.
    /// Empty is a valid value for passing evaluations.
    /// </summary>
    public IReadOnlyList<string> FindingIds { get; init; }
        = Array.Empty<string>();

    public DateTimeOffset EvaluatedAt { get; init; }
        = DateTimeOffset.UtcNow;
}