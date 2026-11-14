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

    /// <summary>
    /// Language-level container identity, such as a Java package, C# namespace,
    /// Python module or TypeScript module. It is supplied by the semantic provider.
    /// </summary>
    public string? ContainerName { get; init; }

    /// <summary>Normalized kind of language-level container.</summary>
    public string? ContainerKind { get; init; }

    public string Language { get; init; } = string.Empty;

    public string ProjectPath { get; init; } = string.Empty;

    public string? FilePath { get; init; }

    public int? StartLine { get; init; }

    public int? StartColumn { get; init; }

    public string? ContainingSymbolId { get; init; }

    public SemanticSymbolOrigin Origin { get; init; }

    public bool IsTestCode { get; init; }

    public bool IsPublic { get; init; }

    /// <summary>Normalized source declaration form, such as DataClass, Object, ExtensionFunction or TypeAlias.</summary>
    public string? DeclarationKind { get; init; }

    /// <summary>Normalized source visibility: Public, Internal, Protected, Private or Unknown.</summary>
    public string? Visibility { get; init; }

    /// <summary>Resolved receiver type for extension declarations.</summary>
    public string? ReceiverType { get; init; }

    /// <summary>Expanded target type for a type-alias declaration.</summary>
    public string? ExpandedType { get; init; }

    /// <summary>Normalized source modifiers that are relevant to semantic interpretation.</summary>
    public List<string> Modifiers { get; init; } = [];

    public List<SemanticEvidenceProvenance> Provenance { get; init; } = [];
}
