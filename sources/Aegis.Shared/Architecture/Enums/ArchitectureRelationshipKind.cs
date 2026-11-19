namespace Aegis.Shared.Architecture.Enums;

/// <summary>
/// Canonical, language-neutral relationship kinds used to classify architecture
/// evidence. Provider-native <see cref="SemanticRelationshipKind"/> values remain
/// unchanged and are mapped into this taxonomy at an explicit normalization boundary.
/// Build-manifest and integration relationships are not semantic symbol references.
/// </summary>
public enum ArchitectureRelationshipKind
{
    Unknown = 0,

    // Build and package topology.
    BuildProjectReference = 100,
    BuildModuleDependency = 101,
    BuildPackageDependency = 102,

    // Language-native semantic relationships.
    Declares = 200,
    Inherits = 201,
    Implements = 202,
    Calls = 203,
    References = 204,
    DependencyInjection = 205,
    Registration = 206,
    DynamicLoading = 207,
    Actualizes = 208,
    UsesAnnotation = 209,

    // HTTP and API integration.
    HttpRequest = 300,
    HttpRoute = 301,

    // Messaging integration.
    MessagePublish = 400,
    MessageConsume = 401,
    MessageSend = 402,

    // Remote procedure calls.
    RpcCall = 500,
    RpcEndpoint = 501,

    // Data integration.
    DataRead = 600,
    DataWrite = 601,
    DataMapping = 602
}
