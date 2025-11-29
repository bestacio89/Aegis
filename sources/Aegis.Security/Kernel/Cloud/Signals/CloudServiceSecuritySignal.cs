using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.Cloud.Signals;

public sealed record CloudServiceSecuritySignal : SecuritySignal
{
    public string Provider { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public bool UsesTLS12 { get; init; }
    public bool PubliclyAccessible { get; init; }
    public bool EncryptionEnabled { get; init; }
    public string? Error { get; init; }

    public CloudServiceSecuritySignal() { }

    public CloudServiceSecuritySignal(
        string provider,
        string serviceName,
        string resourceId,
        bool usesTls12,
        bool publiclyAccessible,
        bool encryptionEnabled,
        string? error = null)
    {
        Provider = provider;
        ServiceName = serviceName;
        ResourceId = resourceId;
        UsesTLS12 = usesTls12;
        PubliclyAccessible = publiclyAccessible;
        EncryptionEnabled = encryptionEnabled;
        Error = error;

        Category = "Cloud";
        DetectorId = $"cloud:svc:{resourceId}";
        Evidence = error ?? $"Service scan: {serviceName}";

        Metadata = new Dictionary<string, object>
        {
            ["provider"] = provider,
            ["service"] = serviceName,
            ["resource"] = resourceId,
            ["tls12"] = usesTls12,
            ["public"] = publiclyAccessible,
            ["encrypted"] = encryptionEnabled
        };
    }
}
