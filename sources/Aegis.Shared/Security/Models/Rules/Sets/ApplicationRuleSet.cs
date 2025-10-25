using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules.Sets;

/// <summary>
/// Strongly typed application-level rule definitions for static and hybrid analysis.
/// Targets logic flaws, crypto misuse, and authentication exposures.
/// </summary>
public sealed class ApplicationRuleSet : SecurityRuleSet
{
    private static readonly SecurityRuleDefinition[] _rules =
    {
        // ============================================================
        // 🧩 Rule: Hardcoded Credentials
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-APP-001",
            title: "Hardcoded Credentials",
            desc: "Detects credentials or API keys embedded in source code, environment files, or configuration artifacts.",
            category: SecurityCategory.Application,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A07:2021",
            cwe: "CWE-798",
            vuln: VulnerabilityType.HardcodedSecret,
            tags: new[] { "credentials", "config", "password", "api-key", "secret" },
            pattern: @"(?i)(password\s*=\s*['""]?[^\s'""]+|api[_-]?key\s*=\s*['""]?[A-Za-z0-9\-]+|secret\s*=\s*['""]?[A-Za-z0-9/+=]+)",
            surface: "Codebase",
            remediation: "Use a secure secret vault (e.g., Azure Key Vault, AWS Secrets Manager). Remove any plaintext secrets from version control.",
            reference: "https://owasp.org/Top10/A07_2021-Identification_and_Authentication_Failures/",
            detection: "Static",
            files: new[] { ".cs", ".json", ".yml", ".env", ".config" }
        ),

        // ============================================================
        // 🔐 Rule: Weak or Deprecated Hashing Algorithms
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-APP-002",
            title: "Weak Hashing Algorithms",
            desc: "Detects usage of insecure hashing algorithms (MD5, SHA1) which are cryptographically broken.",
            category: SecurityCategory.Application,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A02:2021",
            cwe: "CWE-327",
            vuln: VulnerabilityType.WeakCryptography,
            tags: new[] { "crypto", "hashing", "security" },
            pattern: @"(?i)\b(MD5|SHA1|SHA-1|RIPEMD160)\b",
            surface: "Application Logic",
            remediation: "Replace with SHA-256, SHA-512, or modern KDFs (bcrypt, scrypt, Argon2). Avoid unsalted hashes entirely.",
            reference: "https://owasp.org/Top10/A02_2021-Cryptographic_Failures/",
            detection: "Static",
            files: new[] { ".cs", ".py", ".java", ".js" }
        ),

        // ============================================================
        // 🧠 Rule: Insecure Random Number Generation
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-APP-003",
            title: "Insecure Random Number Generation",
            desc: "Detects usage of predictable random generators (System.Random, Math.random, etc.) in security-sensitive contexts.",
            category: SecurityCategory.Application,
            severity: SecuritySeverity.Medium,
            risk: RiskLevel.Moderate,
            owasp: "A02:2021",
            cwe: "CWE-330",
            vuln: VulnerabilityType.WeakCryptography,
            tags: new[] { "crypto", "random", "predictability" },
            pattern: @"(?i)(new\s+Random\(|Math\.random|random\(\))",
            surface: "Application Logic",
            remediation: "Use a cryptographically secure RNG (e.g., RNGCryptoServiceProvider, RandomNumberGenerator).",
            reference: "https://cwe.mitre.org/data/definitions/330.html",
            detection: "Static",
            files: new[] { ".cs", ".js", ".java", ".py" }
        ),

        // ============================================================
        // 🛡️ Rule: Missing Input Validation
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-APP-004",
            title: "Missing Input Validation",
            desc: "Detects potential lack of input sanitization or validation on user-controlled data sources.",
            category: SecurityCategory.Application,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A01:2021",
            cwe: "CWE-20",
            vuln: VulnerabilityType.Injection,
            tags: new[] { "input", "validation", "sanitization" },
            pattern: @"(?i)(Request\.QueryString|Request\.Form|Console\.ReadLine|input\(|getParameter\()",
            surface: "Input Interfaces",
            remediation: "Validate and sanitize all external inputs. Apply encoding before output. Use frameworks that auto-sanitize parameters.",
            reference: "https://owasp.org/Top10/A01_2021-Broken_Access_Control/",
            detection: "Hybrid",
            files: new[] { ".cs", ".js", ".py", ".java" }
        ),

        // ============================================================
        // 💥 Rule: Information Disclosure in Logging
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-APP-005",
            title: "Information Disclosure via Logging",
            desc: "Detects personal or confidential data being written directly to logs.",
            category: SecurityCategory.Application,
            severity: SecuritySeverity.Medium,
            risk: RiskLevel.Moderate,
            owasp: "A09:2021",
            cwe: "CWE-532",
            vuln: VulnerabilityType.InformationDisclosure,
            tags: new[] { "logging", "privacy", "pii" },
            pattern: @"(?i)(logger\.(info|debug|trace)\s*\([^)]*(password|token|email|ssn)[^)]*\))",
            surface: "Application Logs",
            remediation: "Avoid logging sensitive data. Sanitize logs or use tokenization for PII fields.",
            reference: "https://owasp.org/Top10/A09_2021-Security_Logging_and_Monitoring_Failures/",
            detection: "Static",
            files: new[] { ".cs", ".java", ".py" }
        )
    };

    public override string Key => "Application";
    public override string Name => "Application Security Rules";
    public override string Description =>
        "Comprehensive rule set for detecting application-level vulnerabilities, including authentication flaws, crypto misuse, and data exposure.";
    public override IReadOnlyCollection<SecurityRuleDefinition> Rules => _rules;
}
