namespace Aegis.Shared.Security.Models;

/// <summary>
/// Human/AI-readable trace that explains why a finding or metric exists.
/// Useful for audits and for ArchenaAI MCP grounding.
/// </summary>
public sealed class SecurityInferenceTrace
{
    /// <summary>Short explanation of the inference path.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Ordered steps or breadcrumbs collected during evaluation.</summary>
    public List<string> Steps { get; init; } = new();

    /// <summary>Optional evidence references (files, lines, external ids, etc.).</summary>
    public Dictionary<string, string> Evidence { get; init; } = new();
}
