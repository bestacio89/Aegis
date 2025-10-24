using System.Text.Json.Serialization;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Top-level security scan artifact for a single project/run.
/// </summary>
public sealed class AegisSecurityReport
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; } = Guid.NewGuid();

    [JsonPropertyName("project")]
    public string ProjectName { get; init; } = string.Empty;

    [JsonPropertyName("scanDate")]
    public DateTimeOffset ScanDate { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>All findings (flat list) produced by rule evaluators.</summary>
    [JsonPropertyName("findings")]
    public List<Rules.SecurityRuleResult> Findings { get; init; } = new();

    /// <summary>High-level summaries grouped by domain/layer/module.</summary>
    [JsonPropertyName("summaries")]
    public List<SecurityDomainSummary> Summaries { get; init; } = new();

    /// <summary>Global metrics (totals, risk index, etc.).</summary>
    [JsonPropertyName("metrics")]
    public GlobalSecurityMetrics Metrics { get; init; } = new();

    public int TotalFindings => Findings.Count;
}
