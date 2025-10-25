using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules.Sets;

/// <summary>
/// Governance and Monitoring rules — enforces auditability, observability, and operational accountability
/// across security logging, metrics, and compliance readiness.
/// </summary>
public sealed class GovernanceRuleSet : SecurityRuleSet
{
    private static readonly SecurityRuleDefinition[] _rules =
    {
        // ============================================================
        // 📜 Rule: Audit Logging Disabled
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-GOV-001",
            title: "Audit Logging Disabled",
            desc: "Detects missing or disabled audit log configurations that prevent traceability of user actions.",
            category: SecurityCategory.Governance,
            severity: SecuritySeverity.Medium,
            risk: RiskLevel.Moderate,
            owasp: "A09:2021",
            cwe: "CWE-778",
            vuln: VulnerabilityType.MissingAuditTrail,
            tags: new[] { "audit", "logging", "monitoring" },
            pattern: @"(?i)(disableAudit|audit\s*=\s*false|logging\s*enabled\s*=\s*false)",
            surface: "System Config",
            remediation: "Enable audit and access logs. Forward events to centralized SIEM for retention and alerting.",
            reference: "https://owasp.org/Top10/A09_2021-Security_Logging_and_Monitoring_Failures/",
            detection: "Static",
            files: new[] { ".json", ".yml", ".conf" }
        ),

        // ============================================================
        // 🧾 Rule: Missing Security Event Correlation
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-GOV-002",
            title: "Missing Security Event Correlation",
            desc: "Detects absence of configuration for event correlation or SIEM forwarding, reducing threat detection capability.",
            category: SecurityCategory.Governance,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A09:2021",
            cwe: "CWE-778",
            vuln: VulnerabilityType.InsufficientLoggingAndMonitoring,
            tags: new[] { "siem", "monitoring", "logging", "correlation" },
            pattern: @"(?i)(splunk|elk|sentinel|datadog|graylog)",
            surface: "Log Forwarding Config",
            remediation: "Integrate with a centralized SIEM or log analytics system. Ensure all security events are ingested and correlated.",
            reference: "https://owasp.org/Top10/A09_2021-Security_Logging_and_Monitoring_Failures/",
            detection: "Static",
            files: new[] { ".yml", ".json", ".conf" }
        ),

        // ============================================================
        // 🔐 Rule: Missing Privileged Operation Monitoring
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-GOV-003",
            title: "Missing Privileged Operation Monitoring",
            desc: "Detects systems not monitoring privileged actions like root access or administrative commands.",
            category: SecurityCategory.Governance,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A05:2021",
            cwe: "CWE-778",
            vuln: VulnerabilityType.BrokenAccessControl,
            tags: new[] { "privilege", "root", "admin", "monitoring" },
            pattern: @"(?i)(sudo|root|admin|elevated)",
            surface: "System Logs / Configs",
            remediation: "Configure audit rules for privileged commands and send them to a secure logging server.",
            reference: "https://owasp.org/Top10/A05_2021-Security_Misconfiguration/",
            detection: "Hybrid",
            files: new[] { ".log", ".conf", ".sh" }
        ),

        // ============================================================
        // 🧩 Rule: Log Integrity Not Enforced
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-GOV-004",
            title: "Log Integrity Not Enforced",
            desc: "Detects absence of log signing, checksum validation, or immutability configuration for logs.",
            category: SecurityCategory.Governance,
            severity: SecuritySeverity.Medium,
            risk: RiskLevel.Moderate,
            owasp: "A09:2021",
            cwe: "CWE-784",
            vuln: VulnerabilityType.SecurityMisconfiguration,
            tags: new[] { "integrity", "log", "immutability", "tamper" },
            pattern: @"(?i)(integrityCheck\s*=\s*false|logSignature\s*=\s*none)",
            surface: "Log Policy / Config",
            remediation: "Use append-only logging and digitally sign or hash logs to prevent tampering.",
            reference: "https://cwe.mitre.org/data/definitions/784.html",
            detection: "Static",
            files: new[] { ".conf", ".json", ".yml" }
        ),

        // ============================================================
        // 🕒 Rule: Log Retention Policy Missing
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-GOV-005",
            title: "Log Retention Policy Missing",
            desc: "Detects missing or undefined log retention period, violating governance and compliance best practices.",
            category: SecurityCategory.Governance,
            severity: SecuritySeverity.Low,
            risk: RiskLevel.Moderate,
            owasp: "A09:2021",
            cwe: "CWE-285",
            vuln: VulnerabilityType.NonCompliantDataRetention,
            tags: new[] { "logging", "policy", "retention" },
            pattern: @"(?i)(retention\s*[:=]\s*(0|none|null|disable))",
            surface: "Configuration Policy",
            remediation: "Define a log retention policy with secure archival and deletion after a compliance-defined duration.",
            reference: "https://owasp.org/Top10/A09_2021-Security_Logging_and_Monitoring_Failures/",
            detection: "Static",
            files: new[] { ".yml", ".json", ".conf" }
        )
    };

    public override string Key => "Governance";
    public override string Name => "Governance & Monitoring Rules";
    public override string Description =>
        "Evaluates observability, audit logging, monitoring, and operational integrity for governance and compliance readiness.";
    public override IReadOnlyCollection<SecurityRuleDefinition> Rules => _rules;
}
