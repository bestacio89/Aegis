using Aegis.Security.Kernel.Cloud.Signals;

namespace Aegis.Security.Kernel.Cloud.Probes;

public sealed class CloudIamProbe
{
    public async Task<CloudIamSecuritySignal> ProbeAsync(
        string provider,
        string principalId,
        CancellationToken ct = default)
    {
        try
        {
            await Task.Delay(20, ct);

            return new CloudIamSecuritySignal(
                provider,
                principalId,
                mfaEnabled: false,
                hasAdminRole: true,
                keyRotationEnabled: false,
                lastKeyRotationUtc: DateTime.UtcNow.AddDays(-120),
                error: null
            );
        }
        catch (Exception ex)
        {
            return new CloudIamSecuritySignal(
                provider,
                principalId,
                mfaEnabled: false,
                hasAdminRole: false,
                keyRotationEnabled: false,
                lastKeyRotationUtc: null,
                error: ex.Message
            );
        }
    }
}
