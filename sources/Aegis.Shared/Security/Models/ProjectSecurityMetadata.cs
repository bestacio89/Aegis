namespace Aegis.Shared.Security.Models;

/// <summary>
/// Represents environmental and contextual metadata captured during a security scan.
/// This metadata links a report to its runtime, configuration, and origin.
/// </summary>
public sealed class ProjectSecurityMetadata
{
    /// <summary>
    /// Unique identifier for the project or system being analyzed.
    /// </summary>
    public string ProjectId { get; init; } = string.Empty;

    /// <summary>
    /// Display name or alias of the project.
    /// </summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>
    /// Optional Git or repository branch under analysis.
    /// </summary>
    public string? Branch { get; init; }

    /// <summary>
    /// Optional commit SHA, tag, or version identifier.
    /// </summary>
    public string? CommitHash { get; init; }

    /// <summary>
    /// Environment name (Development, Staging, Production, etc.).
    /// </summary>
    public string Environment { get; init; } = "Unknown";

    /// <summary>
    /// Machine or host executing the scan.
    /// </summary>
    public string HostName { get; init; } = System.Environment.MachineName;

    /// <summary>
    /// Operating system and version information.
    /// </summary>
    public string OSVersion { get; init; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    /// <summary>
    /// Timestamp of when the scan was initiated.
    /// </summary>
    public DateTimeOffset ScanStartUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp of when the scan finished.
    /// </summary>
    public DateTimeOffset ScanEndUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Aegis Security engine build or release version used.
    /// </summary>
    public string EngineVersion { get; init; } = "1.0.0";

    /// <summary>
    /// Policy or configuration profile used for this scan.
    /// </summary>
    public string PolicyProfile { get; init; } = "Default";

    /// <summary>
    /// Identity of the user or service principal that initiated the scan.
    /// </summary>
    public string ExecutedBy { get; init; } = "system";

    /// <summary>
    /// Optional repository URL or source reference.
    /// </summary>
    public string? RepositoryUrl { get; init; }

    /// <summary>
    /// Optional custom tags for traceability (e.g., ticket IDs, environment markers).
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Returns a compact summary of key metadata elements.
    /// </summary>
    public override string ToString() =>
        $"{ProjectName} [{Environment}] - Branch: {Branch ?? "N/A"}, Commit: {CommitHash ?? "N/A"} | Host: {HostName}, Engine: {EngineVersion}";

    public float RiskIndex { get; init; }
}
