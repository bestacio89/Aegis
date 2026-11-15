namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Compiler output evidence extracted from an explicit, statically understood
/// Gradle task configuration. The directory is project-owned and build-file-backed.
/// </summary>
public sealed record ProjectCompilationOutput(
    string TaskName,
    string Language,
    string? VariantName,
    ProjectCompilationOutputScope Scope,
    string Directory,
    string EvidenceFile);
