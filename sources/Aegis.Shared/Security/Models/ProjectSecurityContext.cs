namespace Aegis.Shared.Security.Models;

/// <summary>
/// Execution context for a security run (paths, environment, input sources).
/// </summary>
public sealed class ProjectSecurityContext
{
    public string ProjectPath { get; init; } = string.Empty;
    public string RepositoryRoot { get; init; } = string.Empty;
    public string Environment { get; init; } = "Default";
    public string? PipelineRunId { get; init; }
    public DateTimeOffset ExecutedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Optional extra context (branch, tags, etc.)</summary>
    public Dictionary<string, string> Tags { get; init; } = new();
    public NetworkProbeConfig NetworkConfig { get; init; } = new();
}
