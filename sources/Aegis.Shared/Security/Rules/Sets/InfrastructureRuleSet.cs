using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules.Sets;

/// <summary>
/// Infrastructure-level security rules — ensures secure platform configuration, encryption, and runtime posture
/// across servers, containers, and network endpoints.
/// </summary>
public sealed class InfrastructureRuleSet : SecurityRuleSet
{
    private static readonly SecurityRuleDefinition[] _rules =
    {
        // ============================================================
        // 🧱 Rule: Outdated Platform Component
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-INF-001",
            title: "Outdated Platform Component",
            desc: "Detects hosts, containers, or runtimes running unsupported or end-of-life versions.",
            category: SecurityCategory.Infrastructure,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A06:2021",
            cwe: "CWE-1104",
            vuln: VulnerabilityType.OutdatedLibrary,
            tags: new[] { "os", "container", "runtime", "version" },
            pattern: @"(?i)(ubuntu\s*(14|16)\.|centos\s*6|python\s*2\.|node\s*10|rhel\s*6)",
            surface: "Platform Configuration",
            remediation: "Upgrade OS and runtime to supported LTS versions. Align with CIS Benchmarks for system hardening.",
            reference: "https://cwe.mitre.org/data/definitions/1104.html",
            detection: "Static",
            files: new[] { "Dockerfile", ".yml", ".tf", ".sh" }
        ),

        // ============================================================
        // 🔐 Rule: Missing TLS for Service Endpoint
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-INF-002",
            title: "Missing TLS for Service Endpoint",
            desc: "Detects HTTP endpoints or service URLs not enforcing TLS (HTTPS).",
            category: SecurityCategory.Infrastructure,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A02:2021",
            cwe: "CWE-319",
            vuln: VulnerabilityType.MissingTLS,
            tags: new[] { "tls", "https", "endpoint", "network" },
            pattern: @"(?i)(http:\/\/(?!localhost|127\.0\.0\.1))",
            surface: "Service Endpoint",
            remediation: "Use HTTPS with TLS 1.2 or higher. Redirect HTTP to HTTPS and enforce HSTS where applicable.",
            reference: "https://owasp.org/Top10/A02_2021-Cryptographic_Failures/",
            detection: "Static",
            files: new[] { ".yml", ".json", ".env", ".config" }
        ),

        // ============================================================
        // 🧩 Rule: Insecure SSH Configuration
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-INF-003",
            title: "Insecure SSH Configuration",
            desc: "Detects SSH configurations that permit root login or use of weak authentication methods.",
            category: SecurityCategory.Infrastructure,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Moderate,
            owasp: "A05:2021",
            cwe: "CWE-276",
            vuln: VulnerabilityType.SecurityMisconfiguration,
            tags: new[] { "ssh", "config", "root", "auth" },
            pattern: @"(?i)(PermitRootLogin\s+yes|PasswordAuthentication\s+yes)",
            surface: "Host Configuration",
            remediation: "Disable root SSH access and password authentication. Enforce key-based SSH and 2FA.",
            reference: "https://www.ssh.com/academy/ssh/security-best-practices",
            detection: "Static",
            files: new[] { "sshd_config", ".conf" }
        ),

        // ============================================================
        // 💾 Rule: Unencrypted Storage Volume
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-INF-004",
            title: "Unencrypted Storage Volume",
            desc: "Detects storage or disk volumes without encryption at rest enabled.",
            category: SecurityCategory.Infrastructure,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A02:2021",
            cwe: "CWE-311",
            vuln: VulnerabilityType.MissingEncryptionAtRest,
            tags: new[] { "storage", "encryption", "volume", "disk" },
            pattern: @"(?i)(encryption\s*=\s*false|Encrypted\s*:\s*false)",
            surface: "Storage Configuration",
            remediation: "Enable encryption on all volumes and snapshots. Use cloud-native KMS or customer-managed keys.",
            reference: "https://cwe.mitre.org/data/definitions/311.html",
            detection: "Static",
            files: new[] { ".json", ".yml", ".bicep", ".tf" }
        ),

        // ============================================================
        // 🌐 Rule: Exposed Management Interface
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-INF-005",
            title: "Exposed Management Interface",
            desc: "Detects cloud or infrastructure management endpoints accessible from the public internet.",
            category: SecurityCategory.Infrastructure,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A05:2021",
            cwe: "CWE-284",
            vuln: VulnerabilityType.UnrestrictedNetworkAccess,
            tags: new[] { "admin", "management", "exposure", "network" },
            pattern: @"(?i)(0\.0\.0\.0\/0|public_ip\s*=\s*true|allow_public_access\s*=\s*true)",
            surface: "Cloud Interface / Admin Panel",
            remediation: "Restrict management endpoints to private networks or VPNs. Require MFA for all administrative access.",
            reference: "https://cwe.mitre.org/data/definitions/284.html",
            detection: "IaC-Parser",
            files: new[] { ".tf", ".bicep", ".yml", ".json" }
        )
    };

    public override string Key => "Infrastructure";
    public override string Name => "Infrastructure Rules";
    public override string Description =>
        "Security rules targeting infrastructure runtime posture — TLS, SSH, IAM, storage, and management interface exposure.";
    public override IReadOnlyCollection<SecurityRuleDefinition> Rules => _rules;
}
