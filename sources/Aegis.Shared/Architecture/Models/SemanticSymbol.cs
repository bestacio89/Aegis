using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// A language-neutral declaration identity. Id is an adapter-generated canonical
/// key that must be deterministic within the project and stable across source-line
/// changes; consumers must treat it as opaque.
/// </summary>
public sealed class SemanticSymbol
{
    public string Id { get; init; } = string.Empty;

    public SemanticSymbolKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public string QualifiedName { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public string ProjectPath { get; init; } = string.Empty;

    public string? FilePath { get; init; }

    public int? StartLine { get; init; }

    public int? StartColumn { get; init; }

    public string? ContainingSymbolId { get; init; }

    public SemanticSymbolOrigin Origin { get; init; }

    public bool IsTestCode { get; init; }

    public bool IsPublic { get; init; }

    public List<SemanticEvidenceProvenance> Provenance { get; init; } = [];
}
