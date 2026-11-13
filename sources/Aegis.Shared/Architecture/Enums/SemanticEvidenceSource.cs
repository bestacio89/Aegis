namespace Aegis.Shared.Architecture.Enums;

/// <summary>
/// Identifies the origin of semantic evidence independently of the language
/// adapter that produced it.
/// </summary>
public enum SemanticEvidenceSource
{
    Unknown = 0,
    Parser = 1,
    Compiler = 2,
    BuildModel = 3,
    FrameworkModel = 4,
    Runtime = 5,
    Inference = 6
}
