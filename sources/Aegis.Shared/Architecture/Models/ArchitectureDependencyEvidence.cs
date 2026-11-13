using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// A normalized dependency declaration, resolution, or runtime observation.
/// This inventory is separate from ArchitectureDependencyContext, which models
/// internal project-to-project architecture graph edges.
/// </summary>
public sealed class ArchitectureDependencyEvidence
{
    /// <summary>Adapter-generated canonical identity, opaque to consumers.</summary>
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Ecosystem { get; init; } = string.Empty;

    public DependencyEvidenceKind Kind { get; init; }

    public string? DeclaredVersion { get; init; }

    public string? ResolvedVersion { get; init; }

    public string Scope { get; init; } = string.Empty;

    public string SourceProjectPath { get; init; } = string.Empty;

    public bool IsDirect { get; init; }

    public bool IsTestOnly { get; init; }

    /// <summary>Whether a platform declaration enforces its dependency versions.</summary>
    public bool IsEnforced { get; init; }

    public DependencyResolutionStatus ResolutionStatus { get; init; }

    public List<SemanticEvidenceProvenance> Provenance { get; init; } = [];
}
