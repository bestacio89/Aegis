using Aegis.Security.Kernel.Cloud.Signals;

namespace Aegis.Security.Kernel.Cloud.Probes;

public sealed class CloudBucketProbe
{
    public async Task<CloudBucketSecuritySignal> ProbeAsync(
        string provider,
        string bucketName,
        CancellationToken ct = default)
    {
        try
        {
            await Task.Delay(20, ct);

            return new CloudBucketSecuritySignal(
                provider,
                bucketName,
                isPublic: false,
                hasEncryption: true,
                versioningEnabled: false,
                loggingEnabled: false,
                error: null
            );
        }
        catch (Exception ex)
        {
            return new CloudBucketSecuritySignal(
                provider,
                bucketName,
                isPublic: false,
                hasEncryption: false,
                versioningEnabled: false,
                loggingEnabled: false,
                error: ex.Message
            );
        }
    }
}
