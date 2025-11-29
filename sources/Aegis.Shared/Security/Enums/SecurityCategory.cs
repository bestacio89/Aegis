namespace Aegis.Shared.Security.Enums;

/// <summary>
/// Represents the high-level classification of security checks,
/// aligned with OWASP, NIST, and MITRE ATT and CK taxonomies.
/// </summary>
public enum SecurityCategory
{
    // =========================================================
    // ⚙️ Application & Code Security
    // =========================================================

    /// <summary>Application-level vulnerabilities (auth, logic flaws, injections, encoding issues).</summary>
    Application = 0,

    /// <summary>Client-side vulnerabilities (DOM-based XSS, CSP misconfigurations, script injection).</summary>
    Frontend = 1,

    /// <summary>API and microservice security issues (improper authorization, rate limiting, schema exposure).</summary>
    API = 2,

    /// <summary>Data storage and transport layer security (encryption, TLS, key management).</summary>
    DataProtection = 3,

    /// <summary>Authentication and identity management weaknesses (weak password policy, JWT misuse, SSO flaws).</summary>
    Identity = 4,

    /// <summary>Authorization and privilege escalation issues (broken access control, mis-scoped roles).</summary>
    Authorization = 5,

    /// <summary>Business logic vulnerabilities (race conditions, bypasses, inconsistent state validation).</summary>
    Logic = 6,
    /// <summary>Filesystem vulnerabilities (race conditions, bypasses, inconsistent state validation).</summary>
    FileSystem = 7,
    // =========================================================
    // ☁️ Infrastructure, IaC, and Cloud
    // =========================================================

    /// <summary>Infrastructure and platform misconfigurations (network, OS, containers).</summary>
    Infrastructure = 10,

    /// <summary>Infrastructure-as-Code and deployment pipeline vulnerabilities.</summary>
    IaC = 11,

    /// <summary>Cloud security configuration issues (IAM roles, storage exposure, key rotation).</summary>
    Cloud = 12,

    /// <summary>Container and orchestration risks (Docker, Kubernetes, Helm, runtime privilege escalation).</summary>
    Container = 13,

    /// <summary>CI/CD pipeline and build system security issues (unsafe artifacts, token leaks).</summary>
    CICD = 14,

    /// <summary>Runtime and workload isolation weaknesses (sandbox escape, privileged processes).</summary>
    Runtime = 15,

    // =========================================================
    // 🌐 Network, Dependencies, and Interfaces
    // =========================================================

    /// <summary>Network layer vulnerabilities (open ports, weak protocols, unsafe firewalls).</summary>
    Network = 20,

    /// <summary>Dependency-related issues (outdated, vulnerable, or unverified third-party components).</summary>
    Dependency = 21,

    /// <summary>Package supply chain risks (typosquatting, malicious packages, poisoned registries).</summary>
    SupplyChain = 22,

    /// <summary>External interface exposures (DNS, FTP, SMTP, VPN, etc.).</summary>
    ExternalInterface = 23,

    // =========================================================
    // 🔒 Secrets, Compliance, and Policy
    // =========================================================

    /// <summary>Secrets and credentials exposed in code or configuration.</summary>
    Secrets = 30,

    /// <summary>Policy compliance and audit checks (GDPR, HIPAA, PCI DSS, ISO, NIST, CIS).</summary>
    Compliance = 31,

    /// <summary>Security governance and organizational policy enforcement.</summary>
    Governance = 32,

    /// <summary>Incident response and audit trail validation (missing logs, disabled alerts).</summary>
    Monitoring = 33,

    /// <summary>Software Bill of Materials (SBOM) verification and license compliance.</summary>
    SBOM = 34,

    // =========================================================
    // 🧠 Advanced & Emerging Domains
    // =========================================================

    /// <summary>AI/ML model security (prompt injection, model poisoning, data leakage).</summary>
    AI = 40,

    /// <summary>Data pipeline and analytics security (ETL, ML Ops, dataset integrity).</summary>
    DataPipeline = 41,

    /// <summary>IoT and embedded systems vulnerabilities (firmware, unsafe comms).</summary>
    IoT = 42,

    /// <summary>Mobile application security (permissions, insecure storage, outdated SDKs).</summary>
    Mobile = 43,

    /// <summary>Cryptographic protocol weaknesses and key misuse.</summary>
    Cryptography = 44,

    /// <summary>Physical access and operational technology (OT) systems.</summary>
    Physical = 45,

    /// <summary>Human factors, phishing, and social engineering exposure.</summary>
    Human = 46,
    OS = 47,
    Stt = 48,

    // =========================================================
    // 🧩 Miscellaneous
    // =========================================================

    /// <summary>Unclassified or experimental findings.</summary>
    Misc = 99,
    Unknown = 100
}
