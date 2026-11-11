namespace Aegis.Shared.Architecture.Enums;

/// <summary>
/// Describes whether a semantic relationship target was resolved. A missing
/// target is not equivalent to a relationship that does not exist.
/// </summary>
public enum SemanticResolutionStatus
{
    NotApplicable = 0,
    Resolved = 1,
    PartiallyResolved = 2,
    Unresolved = 3
}
