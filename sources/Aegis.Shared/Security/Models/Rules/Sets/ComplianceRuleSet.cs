using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules.Sets;

/// <summary>
/// Defines compliance-oriented rules for privacy, auditability, and data protection laws (GDPR, HIPAA, ISO 27001, PCI-DSS).
/// </summary>
public sealed class ComplianceRuleSet : SecurityRuleSet
{
    private static readonly SecurityRuleDefinition[] _rules =
    {
        // ============================================================
        // 🧾 Rule: PII Data Logging (GDPR Art. 32)
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-COMP-001",
            title: "PII Data Logging",
            desc: "Detects personal data logged in plaintext violating GDPR and similar privacy regulations.",
            category: SecurityCategory.Compliance,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "GDPR-32",
            cwe: "CWE-532",
            vuln: VulnerabilityType.InformationDisclosure,
            tags: new[] { "logging", "privacy", "pii", "gdpr" },
            pattern: @"(?i)(logger\.(info|debug|trace)\s*\([^)]*(name|email|phone|ssn|passport|pii)[^)]*\))",
            surface: "Application Logs",
            remediation: "Mask or pseudonymize sensitive data before logging; use structured logs with field-level redaction.",
            reference: "https://gdpr-info.eu/art-32-gdpr/",
            detection: "Static",
            files: new[] { ".cs", ".java", ".log" }
        ),

        // ============================================================
        // 🗄️ Rule: Unencrypted Data Storage (GDPR + HIPAA)
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-COMP-002",
            title: "Unencrypted Data Storage",
            desc: "Detects unencrypted persistence of PII or PHI data violating GDPR and HIPAA encryption requirements.",
            category: SecurityCategory.Compliance,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "HIPAA-164.312",
            cwe: "CWE-311",
            vuln: VulnerabilityType.MissingEncryptionAtRest,
            tags: new[] { "storage", "gdpr", "hipaa", "encryption" },
            pattern: @"(?i)(insert\s+into\s+\w+\s*\(.*(name|email|ssn|dob).*|File\.Write(AllText|Bytes)\()",
            surface: "Persistent Storage",
            remediation: "Encrypt all data at rest using AES-256 or higher; ensure key management follows NIST SP 800-57.",
            reference: "https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/",
            detection: "Static",
            files: new[] { ".sql", ".cs", ".java" }
        ),

        // ============================================================
        // 📜 Rule: Missing Audit Trail
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-COMP-003",
            title: "Missing Audit Trail",
            desc: "Detects absence of audit log or modification tracking mechanisms, violating ISO 27001 and SOC 2 controls.",
            category: SecurityCategory.Compliance,
            severity: SecuritySeverity.Medium,
            risk: RiskLevel.Moderate,
            owasp: "ISO-27001-A12.4",
            cwe: "CWE-778",
            vuln: VulnerabilityType.MissingAuditTrail,
            tags: new[] { "audit", "log", "trail", "soc2" },
            pattern: @"(?i)(update|delete)\s+\w+\s*(set|from)\s+\w+",
            surface: "Audit Mechanism",
            remediation: "Implement immutable audit logs with timestamps and user context; store logs in tamper-evident systems.",
            reference: "https://www.iso.org/isoiec-27001-information-security.html",
            detection: "Static",
            files: new[] { ".sql", ".cs", ".java" }
        ),

        // ============================================================
        // 💳 Rule: Unmasked Credit Card or Financial Data (PCI-DSS)
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-COMP-004",
            title: "Unmasked Credit Card or Financial Data",
            desc: "Detects unmasked financial data in code or logs, violating PCI-DSS storage requirements.",
            category: SecurityCategory.Compliance,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "PCI-DSS-3.4",
            cwe: "CWE-319",
            vuln: VulnerabilityType.SensitiveDataExposure,
            tags: new[] { "payment", "card", "pci", "finance" },
            pattern: @"(?<!\d)(\d{13,19})(?!\d)", // card number patterns
            surface: "Application Storage / Logs",
            remediation: "Mask card numbers (####-####-####-1234) and tokenize sensitive payment data before persistence.",
            reference: "https://www.pcisecuritystandards.org/",
            detection: "Static",
            files: new[] { ".cs", ".log", ".json" }
        ),

        // ============================================================
        // 🧩 Rule: Missing Data Retention Policy
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-COMP-005",
            title: "Missing Data Retention Policy",
            desc: "Detects lack of defined data retention or disposal mechanisms, required by GDPR and ISO 27001.",
            category: SecurityCategory.Compliance,
            severity: SecuritySeverity.Low,
            risk: RiskLevel.Moderate,
            owasp: "GDPR-5",
            cwe: "CWE-285",
            vuln: VulnerabilityType.NonCompliantDataRetention,
            tags: new[] { "policy", "retention", "data", "gdpr" },
            pattern: @"(?i)(archive|delete|purge|cleanup)",
            surface: "Lifecycle Policy",
            remediation: "Establish explicit retention periods and automated purging for obsolete records or backups.",
            reference: "https://gdpr-info.eu/art-5-gdpr/",
            detection: "Review",
            files: new[] { ".md", ".yml", ".json" }
        )
    };

    public override string Key => "Compliance";
    public override string Name => "Compliance Enforcement Rules";
    public override string Description =>
        "Evaluates privacy, audit, and compliance enforcement across data, logs, and lifecycle governance (GDPR, HIPAA, PCI-DSS, ISO 27001).";
    public override IReadOnlyCollection<SecurityRuleDefinition> Rules => _rules;
}
