namespace Aegis.Shared.Architecture.Enums;

/// <summary>
/// Language-neutral declaration categories used by semantic symbol evidence.
/// Adapters map language-specific syntax onto these categories without exposing
/// parser or compiler types to shared architecture consumers.
/// </summary>
public enum SemanticSymbolKind
{
    Unknown = 0,
    Package = 1,
    Namespace = 2,
    Module = 3,
    Class = 4,
    Interface = 5,
    Record = 6,
    Enum = 7,
    Annotation = 8,
    Method = 9,
    Constructor = 10,
    Field = 11,
    Property = 12,
    Parameter = 13,
    TypeAlias = 14
}
