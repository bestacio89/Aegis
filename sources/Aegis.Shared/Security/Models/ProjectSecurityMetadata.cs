namespace Aegis.Shared.Security.Models;

/// <summary>
/// Descriptive metadata about the project/repo being analyzed.
/// </summary>
public sealed class ProjectSecurityMetadata
{
    public string ProjectName { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string SourceControlUrl { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string CommitHash { get; init; } = string.Empty;

    /// <summary>Optional: package manager(s), runtime target(s), language(s).</summary>
    public string[] TechStack { get; init; } = Array.Empty<string>();
}
