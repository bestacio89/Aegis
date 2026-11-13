namespace Aegis.Shared.Architecture.Enums;

/// <summary>Classifies the kind of dependency declared, resolved, or observed.</summary>
public enum DependencyEvidenceKind
{
    Unknown = 0,
    Project = 1,
    Package = 2,
    Platform = 3,
    Plugin = 4,
    RuntimeLibrary = 5
}
