using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Represents a causal or correlated link between two or more security findings,
/// providing traceability and impact inference across the system.
/// </summary>
public sealed class SecurityInferenceTrace
{
    /// <summary>
    /// Unique identifier for this inference trace.
    /// </summary>
    public Guid TraceId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The originating finding (root cause).
    /// </summary>
    public string SourceRuleId { get; init; } = string.Empty;

    /// <summary>
    /// The affected or derived finding (consequence or propagated effect).
    /// </summary>
    public string TargetRuleId { get; init; } = string.Empty;

    /// <summary>
    /// Describes the nature of the relationship (Causal, Correlated, Propagated, etc.).
    /// </summary>
    public InferenceRelationType Relation { get; init; }

    /// <summary>
    /// Optional description explaining how the relation was derived or detected.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Severity of the resulting impact caused by this inference.
    /// </summary>
    public SecuritySeverity ImpactSeverity { get; init; }

    /// <summary>
    /// Optional list of intermediary nodes (e.g., dependencies, services, or files)
    /// through which this inference propagated.
    /// </summary>
    public IReadOnlyCollection<string> Path { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional evidence or reasoning used to generate the link (e.g., shared API key, same resource, log pattern, etc.).
    /// </summary>
    public string? Evidence { get; init; }

    /// <summary>
    /// UTC timestamp when the inference was generated.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Compact string summary for logs and dashboards.
    /// </summary>
    public override string ToString() =>
        $"[{Relation}] {SourceRuleId} → {TargetRuleId} ({ImpactSeverity})";
}

/// <summary>
/// Defines the type of relationship between two or more findings in an inference trace.
/// </summary>
public enum InferenceRelationType
{
    /// <summary>One vulnerability directly causes another.</summary>
    Causal = 0,

    /// <summary>Findings are strongly correlated (same source, vector, or condition).</summary>
    Correlated = 1,

    /// <summary>Vulnerability propagates through dependencies, network, or shared resources.</summary>
    Propagated = 2,

    /// <summary>Represents a mitigating or neutralizing relationship (e.g., patch applied).</summary>
    Mitigated = 3,

    /// <summary>Represents detected false-positive linkage or invalid correlation.</summary>
    Invalidated = 4
}
