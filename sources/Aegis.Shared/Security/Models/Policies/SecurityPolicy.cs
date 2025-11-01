// Aegis.Shared/Security/Models/Policies/SecurityPolicyRegistry.cs
using System.Net;

namespace Aegis.Shared.Security.Models.Policies;

public static class SecurityPolicyRegistry
{
    private static readonly IReadOnlyDictionary<string, AegisSecurityPolicy> _byDomain =
        new Dictionary<string, AegisSecurityPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            ["Application"] = new Application.ApplicationSecurityPolicy(),
            ["Dependency"] = new Dependency.DependencySecurityPolicy(),
            ["Governance"] = new Governance.GovernanceSecurityPolicy(),
            ["IaC"] = new IaC.IaCSecurityPolicy(),
            ["Infrastructure"] = new Infrastructure.InfrastructureSecurityPolicy(),
            ["Network"] = new Network.NetworkSecurityPolicy(),
            ["Secrets"] = new Secrets.SecretsSecurityPolicy(),
        };

    public static AegisSecurityPolicy Get(string domain)
        => _byDomain.TryGetValue(domain, out var p)
            ? p
            : throw new KeyNotFoundException($"Unknown security policy domain '{domain}'.");

    public static IEnumerable<AegisSecurityPolicy> All() => _byDomain.Values;
}
