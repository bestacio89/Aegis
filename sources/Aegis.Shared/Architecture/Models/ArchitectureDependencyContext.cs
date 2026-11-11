namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Describes one detector-reported dependency between two discovered projects.
/// Scope distinguishes production architecture from test-only evidence.
/// </summary>
public sealed class ArchitectureDependencyContext
{
    public string Source { get; set; } = string.Empty;

    public string SourceLayer { get; set; } = string.Empty;

    public string SourceProjectPath { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string TargetLayer { get; set; } = string.Empty;

    public string TargetProjectPath { get; set; } = string.Empty;

    public string DependencyType { get; set; } = string.Empty;

    /// <summary>
    /// Production when both endpoints are production projects; Test when either
    /// endpoint is a test project. Test relationships remain evidence but are
    /// excluded from the production dependency graph.
    /// </summary>
    public string Scope { get; set; } = "Production";

    public string File { get; set; } = string.Empty;
}
