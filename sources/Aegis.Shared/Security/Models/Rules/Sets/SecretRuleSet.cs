using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules.Sets;

/// <summary>
/// Secrets and credentials rule set — detects hardcoded or leaked credentials, keys, and tokens
/// across source code, configurations, and repositories.
/// </summary>
public sealed class SecretsRuleSet : SecurityRuleSet
{
    private static readonly SecurityRuleDefinition[] _rules =
    {
        // ============================================================
        // 🔑 Rule: Private Key Leakage
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-SEC-001",
            title: "Private Key Leakage",
            desc: "Detects the presence of private keys or certificates embedded in the codebase.",
            category: SecurityCategory.Secrets,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A07:2021",
            cwe: "CWE-324",
            vuln: VulnerabilityType.SecretExposure,
            tags: new[] { "secrets", "keys", "certs" },
            pattern: @"-----BEGIN (?:RSA |EC )?PRIVATE KEY-----",
            surface: "Source Code / Repository",
            remediation: "Remove the key immediately and rotate associated credentials. Use secure vaults (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault).",
            reference: "https://owasp.org/Top10/A07_2021-Identification_and_Authentication_Failures/",
            detection: "Static",
            files: new[] { ".pem", ".key", ".crt", ".cs", ".json", ".yml" }
        ),

        // ============================================================
        // 🧩 Rule: Hardcoded Password or Token
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-SEC-002",
            title: "Hardcoded Password or Token",
            desc: "Detects hardcoded passwords, tokens, or authentication credentials in code or config.",
            category: SecurityCategory.Secrets,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A07:2021",
            cwe: "CWE-798",
            vuln: VulnerabilityType.HardcodedSecret,
            tags: new[] { "password", "token", "auth", "secret" },
            pattern: @"(?i)(password\s*=\s*\"".+\""|token\s*=\s*\"".+\""|api[_-]?key\s*=\s*\"".+\"")",
            surface: "Code / Configuration",
            remediation: "Use environment variables or secret references; never store secrets in source control.",
            reference: "https://owasp.org/Top10/A07_2021-Identification_and_Authentication_Failures/",
            detection: "Static",
            files: new[] { ".cs", ".py", ".java", ".env", ".yml", ".json" }
        ),

        // ============================================================
        // ☁️ Rule: Cloud Provider Key Leakage
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-SEC-003",
            title: "Cloud Provider Key Leakage",
            desc: "Detects leaked API keys or access tokens for major cloud providers (AWS, Azure, GCP).",
            category: SecurityCategory.Secrets,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A07:2021",
            cwe: "CWE-200",
            vuln: VulnerabilityType.SecretExposure,
            tags: new[] { "aws", "azure", "gcp", "token", "apikey" },
            pattern: @"(?i)(AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z\-_]{35}|ASIA[0-9A-Z]{16})",
            surface: "Code / Config / History",
            remediation: "Revoke compromised keys immediately and regenerate them with least-privilege permissions.",
            reference: "https://docs.aws.amazon.com/general/latest/gr/aws-access-keys-best-practices.html",
            detection: "Static",
            files: new[] { ".cs", ".env", ".json", ".yml", ".py" }
        ),

        // ============================================================
        // 🧰 Rule: Exposed SSH Private Key
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-SEC-004",
            title: "Exposed SSH Private Key",
            desc: "Detects SSH private keys mistakenly committed to repositories or configs.",
            category: SecurityCategory.Secrets,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A05:2021",
            cwe: "CWE-532",
            vuln: VulnerabilityType.SecretExposure,
            tags: new[] { "ssh", "privatekey", "git", "credentials" },
            pattern: @"-----BEGIN OPENSSH PRIVATE KEY-----",
            surface: "Repository / Configuration",
            remediation: "Purge from history using Git BFG or `git filter-repo`, rotate SSH keys, and update authorized keys.",
            reference: "https://cwe.mitre.org/data/definitions/532.html",
            detection: "Static",
            files: new[] { ".ssh", ".key", ".txt", ".log", ".gitignore" }
        ),

        // ============================================================
        // 🧾 Rule: Secrets Committed to Git History
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-SEC-005",
            title: "Secrets Committed to Git History",
            desc: "Detects previously deleted secrets still present in Git history or commits.",
            category: SecurityCategory.Secrets,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A09:2021",
            cwe: "CWE-200",
            vuln: VulnerabilityType.SecretExposure,
            tags: new[] { "git", "history", "secrets" },
            pattern: @"(?i)(password|api[_-]?key|secret|token).*?(""?[A-Za-z0-9/_+=-]{8,}"")",
            surface: "Git Repository History",
            remediation: "Use tools like TruffleHog or GitLeaks to scan history. Rotate any affected credentials immediately.",
            reference: "https://owasp.org/Top10/A09_2021-Security_Logging_and_Monitoring_Failures/",
            detection: "Dynamic",
            files: new[] { ".git", ".log", ".json" }
        )
    };

    public override string Key => "Secrets";
    public override string Name => "Secrets and Credential Rules";
    public override string Description =>
        "Detects leaked credentials, private keys, tokens, and other sensitive information across code and repositories.";
    public override IReadOnlyCollection<SecurityRuleDefinition> Rules => _rules;
}
