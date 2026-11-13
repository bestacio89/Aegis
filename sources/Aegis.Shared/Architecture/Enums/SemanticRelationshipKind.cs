namespace Aegis.Shared.Architecture.Enums;

/// <summary>
/// Normalized semantic relationships between declarations. Build-manifest
/// relationships remain represented separately by architecture dependency evidence.
/// </summary>
public enum SemanticRelationshipKind
{
    Unknown = 0,
    Declares = 1,
    Inherits = 2,
    Implements = 3,
    Calls = 4,
    References = 5,
    DependsOn = 6,
    UsesAnnotation = 7,
    Injects = 8,
    Registers = 9,
    LoadsDynamically = 10
}
