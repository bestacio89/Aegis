using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// A framework or technology observation attributed to a project and its
/// supporting evidence. Name alone is not sufficient to establish architecture role.
/// </summary>
public sealed class FrameworkEvidence
{
    /// <summary>Adapter-generated canonical identity, opaque to consumers.</summary>
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Version { get; init; }

    public FrameworkEvidenceKind Kind { get; init; }

    public string ProjectPath { get; init; } = string.Empty;

    public bool IsTestOnly { get; init; }

    public List<SemanticEvidenceProvenance> Provenance { get; init; } = [];
}
