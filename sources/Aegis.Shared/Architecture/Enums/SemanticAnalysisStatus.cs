namespace Aegis.Shared.Architecture.Enums;

/// <summary>
/// Describes the outcome of a semantic-analysis attempt. Unsupported and failed
/// analysis are distinct from partial analysis and from analysis that was not requested.
/// </summary>
public enum SemanticAnalysisStatus
{
    NotRequested = 0,
    Unsupported = 1,
    Completed = 2,
    PartiallyCompleted = 3,
    Failed = 4
}
