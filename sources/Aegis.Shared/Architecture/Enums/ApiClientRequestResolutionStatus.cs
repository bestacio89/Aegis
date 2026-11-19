namespace Aegis.Shared.Architecture.Enums;

/// <summary>Describes how much of a client request target can be established statically.</summary>
public enum ApiClientRequestResolutionStatus
{
    Resolved,
    PartiallyResolved,
    Dynamic,
    Unsupported
}
