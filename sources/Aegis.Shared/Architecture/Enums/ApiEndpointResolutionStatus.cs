namespace Aegis.Shared.Architecture.Enums;

/// <summary>Describes how precisely a server endpoint route was resolved.</summary>
public enum ApiEndpointResolutionStatus
{
    Resolved = 1,
    PartiallyResolved = 2,
    Dynamic = 3,
    Unsupported = 4
}
