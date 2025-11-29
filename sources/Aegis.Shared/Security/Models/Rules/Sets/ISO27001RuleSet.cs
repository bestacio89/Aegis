using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models.Rules;
using Aegis.Shared.Security.Models.Rules.Sets;

namespace Aegis.Security.Rules.ISO;

/// <summary>
/// ISO/IEC 27001:2022 Technical Controls — automated checks only.
/// Organizational/policy-oriented controls are excluded or marked as non-automatable.
/// </summary>
public sealed class ISO27001RuleSet : SecurityRuleSet
{
    private static readonly SecurityRuleDefinition[] _rules =
    {
        // ==========================================================================================
        // A.5 — Organizational Controls  (Partially automatable)
        // ==========================================================================================

        SecurityRuleSetFactory.CreateISO(
            id: "ISO-5.17-001",
            title: "Default or Guest Accounts Enabled",
            desc: "Detects default OS guest accounts that violate ISO 27001 identity hardening.",
            category: SecurityCategory.OS,
            severity: SecuritySeverity.High,
            risk: RiskLevel.High,
            owasp: null,
            cwe: "CWE-266",
            vuln: VulnerabilityType.BrokenAccessControl,
            tags: new[] { "identity", "accounts", "os", "authentication" },
            pattern: @"(?i)(GuestAccountEnabled|defaultUser)",
            surface: "OS Security Configuration",
            remediation: "Disable default/guest accounts immediately.",
            reference: "https://www.iso.org/standard/82875.html",
            isoControl: "A.5.17",
            isoClause: "Identity Management",
            cis: "CIS 1.1",
            detection: "Dynamic"
        ),

        // ==========================================================================================
        // A.8 — Technological Controls
        // ==========================================================================================

        SecurityRuleSetFactory.CreateISO(
            id: "ISO-8.1-001",
            title: "Unencrypted Sensitive Files",
            desc: "Detects unencrypted sensitive files (keys, secrets) stored on endpoints.",
            category: SecurityCategory.FileSystem,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Moderate,
            owasp: null,
            cwe: "CWE-311",
            vuln: VulnerabilityType.Stt,
            tags: new[] { "filesystem", "encryption", "key", "secret" },
            pattern: @"(?i)(BEGIN PRIVATE KEY|password=|jwt|token|apikey)",
            surface: "File System",
            remediation: "Encrypt or remove unencrypted sensitive files.",
            reference: "https://www.cisecurity.org/controls",
            isoControl: "A.8.1",
            isoClause: "User Endpoint Security",
            cis: "CIS 3.4",
            detection: "Static",
            files: new[] { ".pem", ".key", ".pfx", ".env" }
        ),

        SecurityRuleSetFactory.CreateISO(
            id: "ISO-8.10-001",
            title: "Audit Logging Disabled",
            desc: "Ensures audit logs are enabled per ISO 8.10.",
            category: SecurityCategory.Governance,
            severity: SecuritySeverity.Medium,
            risk: RiskLevel.Moderate,
            owasp: null,
            cwe: "CWE-778",
            vuln: VulnerabilityType.InsufficientLoggingAndMonitoring,
            tags: new[] { "audit", "logging", "monitoring" },
            pattern: @"(?i)(audit\s*=\s*false|AuditDisabled)",
            surface: "Configs / Policies",
            remediation: "Enable audit logs and secure retention.",
            reference: "https://owasp.org/Top10/A09_2021-Security_Logging_and_Monitoring_Failures/",
            isoControl: "A.8.10",
            isoClause: "Logging",
            cis: "CIS 6.1",
            detection: "Static"
        ),

        // ==========================================================================================
        // A.12 — Operations Security (Highly automatable)
        // ==========================================================================================

        SecurityRuleSetFactory.CreateISO(
            id: "ISO-12.2-001",
            title: "Suspicious High-Memory Process",
            desc: "Detects suspicious long-running high-memory processes.",
            category: SecurityCategory.OS,
            severity: SecuritySeverity.High,
            risk: RiskLevel.High,
            owasp: null,
            cwe: "CWE-506",
            vuln: VulnerabilityType.PossibleMalware,
            tags: new[] { "malware", "process", "os" },
            pattern: @"(?i)(nc\.exe|powershell\.exe|cmd\.exe)",
            surface: "Runtime Processes",
            remediation: "Investigate the process for malware/abuse.",
            reference: "https://attack.mitre.org/",
            isoControl: "A.12.2",
            isoClause: "Malware Protection",
            detection: "Dynamic"
        ),

        SecurityRuleSetFactory.CreateISO(
            id: "ISO-12.6-001",
            title: "Vulnerable Dependencies Detected",
            desc: "Detects outdated or vulnerable dependencies.",
            category: SecurityCategory.Dependency,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: null,
            cwe: "CWE-937",
            vuln: VulnerabilityType.OutdatedDependency,
            tags: new[] { "dependency", "package", "versioning" },
            pattern: @"(?i)(CVE-|outdated|deprecated)",
            surface: "Dependencies",
            remediation: "Upgrade vulnerable dependency versions.",
            reference: "https://cve.mitre.org/",
            isoControl: "A.12.6",
            isoClause: "Technical Vulnerability Management",
            cis: "CIS 2.3",
            detection: "Static"
        ),

        // ==========================================================================================
        // A.13 — Communications Security
        // ==========================================================================================

        SecurityRuleSetFactory.CreateISO(
            id: "ISO-13.2-001",
            title: "Weak TLS Protocol Detected",
            desc: "Detects TLS versions < TLS 1.2",
            category: SecurityCategory.Network,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: null,
            cwe: "CWE-326",
            vuln: VulnerabilityType.WeakEncryption,
            tags: new[] { "tls", "ssl", "network" },
            pattern: @"(?i)(SSL3|TLS1\.0|TLS1\.1)",
            surface: "TLS",
            remediation: "Enforce TLS 1.2 or 1.3.",
            reference: "https://cwe.mitre.org/data/definitions/326.html",
            isoControl: "A.13.2",
            isoClause: "Secure Transmission",
            cis: "CIS 4.9",
            detection: "Dynamic"
        ),

        // ==========================================================================================
        // A.14 — System Acquisition, Development & Maintenance
        // ==========================================================================================

        SecurityRuleSetFactory.CreateISO(
            id: "ISO-14.1-001",
            title: "Hardcoded Secrets in Source Code",
            desc: "Detects environment secrets hardcoded into code.",
            category: SecurityCategory.Application,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.High,
            owasp: null,
            cwe: "CWE-798",
            vuln: VulnerabilityType.HardcodedSecret,
            tags: new[] { "secrets", "source", "sdlc" },
            pattern: @"(?i)(password\s*=|apikey|jwt|bearer\s+[A-Za-z0-9])",
            surface: "Source Code",
            remediation: "Replace secrets with environment variables or vault.",
            reference: "https://cwe.mitre.org/data/definitions/798.html",
            isoControl: "A.14.1",
            isoClause: "Secure Development Lifecycle",
            cis: "CIS 16.12",
            detection: "Static"
        )
    };

    public override string Key => "ISO27001";
    public override string Name => "ISO/IEC 27001:2022 Automated Controls";
    public override string Description =>
        "Technical controls from ISO/IEC 27001 Annex A mapped to automatic Aegis scans.";
    public override IReadOnlyCollection<SecurityRuleDefinition> Rules => _rules;
}
