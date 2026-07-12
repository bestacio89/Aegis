namespace Aegis.Security.Bootstrap;

public sealed class AegisSecurityOptions
{
    /// <summary>Enable/disable specific probe groups.</summary>
    public bool EnableOsProbes { get; set; } = true;
    public bool EnableNetworkProbes { get; set; } = true;
    public bool EnableTlsProbes { get; set; } = true;
    public bool EnableFileSystemProbes { get; set; } = true;
    public bool EnableCloudProbes { get; set; } = true;
    public bool EnableWcfProbes { get; set; } = true;

    /// <summary>Engine version & policy version injected into reports.</summary>
    public string EngineVersion { get; set; } = "1.0.0-alpha";
    public string PolicyVersion { get; set; } = "v1.0";
}
