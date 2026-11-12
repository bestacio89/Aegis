namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Describes an application-level integration between independently discovered
/// modules. Unlike build dependencies, integrations represent runtime contracts
/// such as HTTP APIs, messaging, streaming, or shared storage.
/// </summary>
public sealed class ArchitectureIntegrationContext
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceModuleName { get; set; } = string.Empty;
    public string SourceModulePath { get; set; } = string.Empty;
    public string TargetModuleName { get; set; } = string.Empty;
    public string TargetModulePath { get; set; } = string.Empty;
    public string InteractionPattern { get; set; } = "RequestResponse";
    public string Protocol { get; set; } = "HTTP";
    public string EvidenceOrigin { get; set; } = "UserDefined";

    public string EndpointResolutionStatus { get; set; } = "Declared";
    public string? Notes { get; set; }
    public DateTimeOffset DeclaredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
