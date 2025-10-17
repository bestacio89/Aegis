namespace Aegis.Shared.Models;

public sealed class EvaluatorResult
{
    /// <summary>
    /// Name of the evaluator that produced this result (e.g. "ComplexityEvaluator").
    /// </summary>
    public string Source { get; set; } = default!;

    /// <summary>
    /// The file, class, or entity analyzed.
    /// </summary>
    public string Target { get; set; } = default!;

    /// <summary>
    /// Arbitrary key-value metrics collected by the evaluator.
    /// Example: { "CyclomaticComplexity": 8, "MaintainabilityIndex": 72 }
    /// </summary>
    public Dictionary<string, double> Metrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional metadata (line number, method name, namespace, etc.)
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Optional hint for contextual grouping (e.g. namespace, layer, module)
    /// </summary>
    public string? Category { get; set; }

    public EvaluatorResult() { }

    public EvaluatorResult(string source, string target)
    {
        Source = source;
        Target = target;
    }

    public override string ToString() =>
        $"{Source} → {Target} [{string.Join(", ", Metrics.Select(m => $"{m.Key}={m.Value:0.##}"))}]";
}
