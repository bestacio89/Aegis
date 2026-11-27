namespace Aegis.Shared.Architecture.Enums;

/// <summary>Describes the static resolution quality of an integration observation.</summary>
public enum IntegrationEvidenceResolutionStatus
{
    Resolved,
    PartiallyResolved,
    Dynamic,
    Unresolved,
    Unsupported
}
