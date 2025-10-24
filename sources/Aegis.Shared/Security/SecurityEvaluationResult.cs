namespace Aegis.Shared.Security.Models;

/// <summary>
/// Result returned by a single evaluator execution (not per rule, but per evaluator unit).
/// </summary>
public sealed class SecurityEvaluationResult
{
    public string EvaluatorName { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public int FindingsCount { get; init; }

    /// <summary>Additional evaluator-specific metrics.</summary>
    public Dictionary<string, double> Metrics { get; init; } = new();

    /// <summary>Optional trace that explains how results were derived.</summary>
    public SecurityInferenceTrace? Trace { get; init; }
}
