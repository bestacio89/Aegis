using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.Cloud.Signals;

public sealed record CloudIamSecuritySignal : SecuritySignal
{
    public string Provider { get; init; } = string.Empty;
    public string PrincipalId { get; init; } = string.Empty;
    public bool MfaEnabled { get; init; }
    public bool HasAdminRole { get; init; }
    public bool KeyRotationEnabled { get; init; }
    public DateTime? LastKeyRotationUtc { get; init; }
    public string? Error { get; init; }

    public CloudIamSecuritySignal() { }

    public CloudIamSecuritySignal(
        string provider,
        string principalId,
        bool mfaEnabled,
        bool hasAdminRole,
        bool keyRotationEnabled,
        DateTime? lastKeyRotationUtc,
        string? error = null)
    {
        Provider = provider;
        PrincipalId = principalId;
        MfaEnabled = mfaEnabled;
        HasAdminRole = hasAdminRole;
        KeyRotationEnabled = keyRotationEnabled;
        LastKeyRotationUtc = lastKeyRotationUtc;
        Error = error;

        Category = "Cloud";
        DetectorId = $"cloud:iam:{principalId}";
        Evidence = error ?? $"IAM scan: {principalId}";

        Metadata = new Dictionary<string, object>
        {
            ["provider"] = provider,
            ["principal"] = principalId,
            ["mfa"] = mfaEnabled,
            ["isAdmin"] = hasAdminRole,
            ["keyRotation"] = keyRotationEnabled,
            ["lastRotation"] = lastKeyRotationUtc
        };
    }
}
