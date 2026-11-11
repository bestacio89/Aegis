namespace Aegis.Shared.Architecture.Enums;

/// <summary>
/// Describes the evidence state of a dependency independently of its declared
/// version or confidence.
/// </summary>
public enum DependencyResolutionStatus
{
    Unknown = 0,
    Declared = 1,
    Resolved = 2,
    PartiallyResolved = 3,
    Unresolved = 4,
    Observed = 5,
    NotApplicable = 6
}
