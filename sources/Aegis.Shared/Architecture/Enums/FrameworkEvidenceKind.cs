namespace Aegis.Shared.Architecture.Enums;

/// <summary>Classifies a framework or technology role without assuming a language.</summary>
public enum FrameworkEvidenceKind
{
    Unknown = 0,
    Application = 1,
    Web = 2,
    Persistence = 3,
    Messaging = 4,
    Serialization = 5,
    Testing = 6,
    BuildPlugin = 7,
    Runtime = 8
}
