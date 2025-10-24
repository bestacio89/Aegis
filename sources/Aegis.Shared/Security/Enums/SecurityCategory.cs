namespace Aegis.Shared.Security.Enums;

/// <summary>
/// Represents the high-level classification of security checks.
/// </summary>
public enum SecurityCategory
{
    /// <summary>Application-level vulnerabilities (code, authentication, logic flaws).</summary>
    Application = 0,

    /// <summary>Infrastructure and platform misconfigurations (network, OS, containers).</summary>
    Infrastructure = 1,

    /// <summary>Dependency-related issues (third-party libraries, outdated packages, etc.).</summary>
    Dependency = 2,

    /// <summary>Secrets and credentials exposed in code or configuration.</summary>
    Secrets = 3,

    /// <summary>Network layer security checks (open ports, weak protocols, TLS configuration).</summary>
    Network = 4,

    /// <summary>Infrastructure-as-Code and deployment pipeline vulnerabilities.</summary>
    IaC = 5,

    /// <summary>Compliance-related checks (GDPR, HIPAA, ISO, NIST, etc.).</summary>
    Compliance = 6,

    /// <summary>Uncategorized or low-confidence findings.</summary>
    Misc = 99
}
