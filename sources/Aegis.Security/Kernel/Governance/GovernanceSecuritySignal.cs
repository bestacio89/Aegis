using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.Governance.Signals;

public sealed record GovernanceSecuritySignal : SecuritySignal
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;

    public GovernanceSecuritySignal() { }

    public GovernanceSecuritySignal(string key, string value)
    {
        Key = key;
        Value = value;

        Category = "Governance";
        DetectorId = $"gov:{key}";
        Evidence = $"{key} = {value}";
    }
}
