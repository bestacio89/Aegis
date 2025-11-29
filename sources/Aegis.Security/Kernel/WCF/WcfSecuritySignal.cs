using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.WCF.Signals;

public sealed record WcfSecuritySignal : SecuritySignal
{
    public string ServiceName { get; init; } = string.Empty;
    public string Binding { get; init; } = string.Empty;
    public bool IsHttps { get; init; }
    public string? SecurityMode { get; init; }
    public string? ClientCredentialType { get; init; }
    public string? AlgorithmSuite { get; init; }
    public bool MetadataExposed { get; init; }
    public bool AllowsAnonymous { get; init; }
    public string? Error { get; init; }

    public WcfSecuritySignal() { }

    public WcfSecuritySignal(
        string serviceName,
        string binding,
        bool isHttps,
        string? securityMode,
        string? clientCredentialType,
        string? algorithmSuite,
        bool metadataExposed,
        bool allowsAnonymous,
        string? error = null)
    {
        ServiceName = serviceName;
        Binding = binding;
        IsHttps = isHttps;
        SecurityMode = securityMode;
        ClientCredentialType = clientCredentialType;
        AlgorithmSuite = algorithmSuite;
        MetadataExposed = metadataExposed;
        AllowsAnonymous = allowsAnonymous;
        Error = error;

        Category = "WCF";
        DetectorId = $"wcf:{serviceName}";
        Evidence = error ?? $"WCF scan: {serviceName}";

        Metadata = new Dictionary<string, object>
        {
            ["service"] = serviceName,
            ["binding"] = binding,
            ["https"] = isHttps,
            ["securityMode"] = securityMode,
            ["clientCredential"] = clientCredentialType,
            ["algorithmSuite"] = algorithmSuite,
            ["metadata"] = metadataExposed,
            ["anonymous"] = allowsAnonymous
        };
    }
}
