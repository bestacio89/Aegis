using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.Cloud.Signals;

public sealed record CloudBucketSecuritySignal : SecuritySignal
{
    public string Provider { get; init; } = string.Empty;
    public string BucketName { get; init; } = string.Empty;
    public bool IsPublic { get; init; }
    public bool HasEncryption { get; init; }
    public bool VersioningEnabled { get; init; }
    public bool LoggingEnabled { get; init; }
    public string? Error { get; init; }

    public CloudBucketSecuritySignal() { }

    public CloudBucketSecuritySignal(
        string provider,
        string bucketName,
        bool isPublic,
        bool hasEncryption,
        bool versioningEnabled,
        bool loggingEnabled,
        string? error = null)
    {
        Provider = provider;
        BucketName = bucketName;
        IsPublic = isPublic;
        HasEncryption = hasEncryption;
        VersioningEnabled = versioningEnabled;
        LoggingEnabled = loggingEnabled;
        Error = error;

        Category = "Cloud";
        DetectorId = $"cloud:bucket:{bucketName}";
        Evidence = error ?? $"Bucket scan: {bucketName}";

        Metadata = new Dictionary<string, object>
        {
            ["provider"] = provider,
            ["bucket"] = bucketName,
            ["public"] = isPublic,
            ["encrypted"] = hasEncryption,
            ["versioning"] = versioningEnabled,
            ["logging"] = loggingEnabled
        };
    }
}
