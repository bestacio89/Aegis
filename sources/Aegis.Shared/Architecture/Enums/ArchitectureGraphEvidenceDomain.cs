namespace Aegis.Shared.Architecture.Enums;

/// <summary>Identifies the authoritative registry containing one graph evidence occurrence.</summary>
public enum ArchitectureGraphEvidenceDomain
{
    SemanticRelationship = 1,
    BuildDependency = 2,
    Messaging = 3,
    Rpc = 4,
    DataAccess = 5,
    ApiClientRequest = 6,
    ApiEndpoint = 7
}
