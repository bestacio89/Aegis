namespace Aegis.Shared.Architecture.Enums;

/// <summary>
/// Architectural responsibilities inferred for a discovered project module.
/// A module may have more than one role, for example a full-stack application.
/// </summary>
public enum ProjectModuleRole
{
    Unknown = 0,
    Backend = 1,
    Frontend = 2,
    Library = 3,
    Shared = 4,
    Test = 5,
    TestSupport = 6,
    Desktop = 7,
    Executable = 8
}
