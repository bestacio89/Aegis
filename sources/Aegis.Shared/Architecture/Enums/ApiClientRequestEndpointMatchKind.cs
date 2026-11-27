namespace Aegis.Shared.Architecture.Enums;

/// <summary>Describes the static evidence used to associate a request with an endpoint candidate.</summary>
public enum ApiClientRequestEndpointMatchKind
{
    ExactPath,
    RouteTemplate,
    UnresolvedEndpoint
}
