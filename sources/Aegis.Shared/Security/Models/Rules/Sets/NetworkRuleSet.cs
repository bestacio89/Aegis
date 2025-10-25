using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules.Sets;

/// <summary>
/// Network exposure rules — evaluates perimeter, TLS posture, and protocol configurations
/// to detect insecure communication paths or publicly accessible services.
/// </summary>
public sealed class NetworkRuleSet : SecurityRuleSet
{
    private static readonly SecurityRuleDefinition[] _rules =
    {
        // ============================================================
        // 🌐 Rule: Open Public Port
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-NET-001",
            title: "Open Public Port",
            desc: "Detects unfiltered TCP/UDP ports exposed to the internet (e.g., SSH, RDP, DB).",
            category: SecurityCategory.Network,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A05:2021",
            cwe: "CWE-200",
            vuln: VulnerabilityType.UnrestrictedNetworkAccess,
            tags: new[] { "network", "portscan", "exposure" },
            pattern: @"(?i)(open port:\s*(22|23|25|80|443|1433|3306|3389|5432))",
            surface: "Perimeter",
            remediation: "Close unused ports and restrict access to internal subnets or VPN tunnels.",
            reference: "https://owasp.org/Top10/A05_2021-Security_Misconfiguration/",
            detection: "Dynamic",
            files: new[] { ".nmap", ".json", ".xml" }
        ),

        // ============================================================
        // 🔐 Rule: Weak TLS/SSL Cipher Detected
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-NET-002",
            title: "Weak TLS/SSL Cipher Detected",
            desc: "Detects deprecated or insecure cryptographic protocols (SSLv2, SSLv3, TLS 1.0/1.1).",
            category: SecurityCategory.Network,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A02:2021",
            cwe: "CWE-326",
            vuln: VulnerabilityType.WeakCryptography,
            tags: new[] { "tls", "ssl", "cipher", "protocol" },
            pattern: @"(?i)(TLSv1\.0|TLSv1\.1|SSLv2|SSLv3|RC4|DES|3DES)",
            surface: "Network Transport Layer",
            remediation: "Enforce TLS 1.2 or higher with modern ciphers (AES-GCM, CHACHA20). Disable legacy protocols.",
            reference: "https://cwe.mitre.org/data/definitions/326.html",
            detection: "Dynamic",
            files: new[] { ".nmap", ".json", ".log" }
        ),

        // ============================================================
        // 📡 Rule: Insecure Protocol Usage
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-NET-003",
            title: "Insecure Protocol Usage",
            desc: "Detects insecure plaintext protocols (FTP, Telnet, HTTP, SMBv1).",
            category: SecurityCategory.Network,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Moderate,
            owasp: "A05:2021",
            cwe: "CWE-319",
            vuln: VulnerabilityType.SecurityMisconfiguration,
            tags: new[] { "ftp", "telnet", "http", "smbv1" },
            pattern: @"(?i)(ftp|telnet|smbv1|http:\/\/(?!localhost|127\.0\.0\.1))",
            surface: "Network Transport Layer",
            remediation: "Replace insecure protocols with encrypted equivalents (SFTP, SSH, HTTPS, SMBv3).",
            reference: "https://owasp.org/Top10/A05_2021-Security_Misconfiguration/",
            detection: "Dynamic",
            files: new[] { ".nmap", ".log", ".txt" }
        ),

        // ============================================================
        // 🧱 Rule: Missing Firewall Egress Restriction
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-NET-004",
            title: "Missing Firewall Egress Restriction",
            desc: "Detects network configurations allowing unrestricted outbound traffic from internal systems.",
            category: SecurityCategory.Network,
            severity: SecuritySeverity.Medium,
            risk: RiskLevel.Moderate,
            owasp: "A05:2021",
            cwe: "CWE-284",
            vuln: VulnerabilityType.SecurityMisconfiguration,
            tags: new[] { "egress", "firewall", "network" },
            pattern: @"(?i)(AllowAllEgress|0\.0\.0\.0/0)",
            surface: "Network Policy / Firewall Rules",
            remediation: "Implement egress filtering to restrict outbound traffic to approved destinations only.",
            reference: "https://cwe.mitre.org/data/definitions/284.html",
            detection: "Static",
            files: new[] { ".tf", ".bicep", ".yml", ".json" }
        ),

        // ============================================================
        // 🧾 Rule: Expired or Invalid Certificate
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-NET-005",
            title: "Expired or Invalid Certificate",
            desc: "Detects expired, self-signed, or invalid SSL/TLS certificates on exposed endpoints.",
            category: SecurityCategory.Network,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A02:2021",
            cwe: "CWE-295",
            vuln: VulnerabilityType.MissingTLS,
            tags: new[] { "certificate", "ssl", "tls", "expiry" },
            pattern: @"(?i)(self-signed|expired|CN\s*mismatch)",
            surface: "SSL/TLS Certificate Metadata",
            remediation: "Renew certificates before expiry and use trusted Certificate Authorities (CAs). Avoid self-signed certs in production.",
            reference: "https://cwe.mitre.org/data/definitions/295.html",
            detection: "Dynamic",
            files: new[] { ".nmap", ".json", ".log" }
        )
    };

    public override string Key => "Network";
    public override string Name => "Network Exposure Rules";
    public override string Description =>
        "Evaluates network posture for open ports, weak TLS, insecure protocols, missing firewalls, and expired certificates.";
    public override IReadOnlyCollection<SecurityRuleDefinition> Rules => _rules;
}
