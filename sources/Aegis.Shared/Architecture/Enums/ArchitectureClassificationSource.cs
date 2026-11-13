namespace Aegis.Shared.Architecture.Enums;

/// <summary>
/// Identifies the evidence source used to classify a code container into a
/// logical architectural component.
/// </summary>
public enum ArchitectureClassificationSource
{
    Unclassified,
    ExplicitMapping,
    Convention,
    Evidence
}
