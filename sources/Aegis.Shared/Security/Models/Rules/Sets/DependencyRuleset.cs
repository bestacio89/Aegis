using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules.Sets;

/// <summary>
/// Dependency vulnerability analysis rules — covering CVEs, unsigned packages, outdated dependencies,
/// and supply chain compromise scenarios across major package managers.
/// </summary>
public sealed class DependencyRuleSet : SecurityRuleSet
{
    private static readonly SecurityRuleDefinition[] _rules =
    {
        // ============================================================
        // 🧩 Rule: Known Vulnerable Library Detected
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-DEP-001",
            title: "Vulnerable Library Detected",
            desc: "Identifies dependencies with known CVEs that exceed criticality thresholds.",
            category: SecurityCategory.Dependency,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A06:2021",
            cwe: "CWE-937",
            vuln: VulnerabilityType.VulnerableDependency,
            tags: new[] { "dependency", "package", "cve", "vulnerability" },
            pattern: @"(?i)CVE-\d{4}-\d+",
            surface: "Dependency Manifest",
            remediation: "Upgrade or replace the library with a patched version. Use trusted mirrors and verify cryptographic signatures.",
            reference: "https://owasp.org/Top10/A06_2021-Vulnerable_and_Outdated_Components/",
            detection: "Dependency-Scanner",
            files: new[] { "package.json", "pom.xml", "requirements.txt", "packages.config", "Cargo.toml" }
        ),

        // ============================================================
        // 🧬 Rule: Outdated Dependency Version
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-DEP-002",
            title: "Outdated Dependency Version",
            desc: "Detects outdated library versions that lag behind the latest stable release by more than N versions.",
            category: SecurityCategory.Dependency,
            severity: SecuritySeverity.Medium,
            risk: RiskLevel.Moderate,
            owasp: "A06:2021",
            cwe: "CWE-1104",
            vuln: VulnerabilityType.OutdatedLibrary,
            tags: new[] { "dependency", "versioning", "update" },
            pattern: @"(?i)(\""?version\""?\s*[:=]\s*\""?\d+\.\d+(\.\d+)?\"")",
            surface: "Package Manifest",
            remediation: "Keep dependencies updated using Dependabot or similar tools; set up continuous version monitoring.",
            reference: "https://cwe.mitre.org/data/definitions/1104.html",
            detection: "Static",
            files: new[] { "package.json", "requirements.txt", "csproj", "pom.xml" }
        ),

        // ============================================================
        // 🧾 Rule: Unsigned or Unverified Package Source
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-DEP-003",
            title: "Unsigned or Unverified Package Source",
            desc: "Detects dependency sources that lack digital signatures or verified publishers.",
            category: SecurityCategory.Dependency,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A06:2021",
            cwe: "CWE-353",
            vuln: VulnerabilityType.UnverifiedArtifactSignature,
            tags: new[] { "signature", "integrity", "supply-chain" },
            pattern: @"(?i)(http://|git://|raw\.githubusercontent\.com)",
            surface: "Package Source",
            remediation: "Use HTTPS with integrity checks (hash or signature). Configure repository whitelists and signed registries.",
            reference: "https://owasp.org/Top10/A06_2021-Vulnerable_and_Outdated_Components/",
            detection: "Static",
            files: new[] { "package-lock.json", "yarn.lock", "Cargo.lock", "go.mod" }
        ),

        // ============================================================
        // 🐍 Rule: Typosquatted or Malicious Package
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-DEP-004",
            title: "Typosquatted or Malicious Package",
            desc: "Detects dependencies with names mimicking popular packages (supply-chain poisoning attempt).",
            category: SecurityCategory.Dependency,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A06:2021",
            cwe: "CWE-829",
            vuln: VulnerabilityType.TyposquattingPackage,
            tags: new[] { "dependency", "supply-chain", "poisoning" },
            pattern: @"(?i)(requests\-|reqests|pandas\-|pands|reactt|react\-domm|lodashh)",
            surface: "Package Manifest",
            remediation: "Validate package names and maintain an allowlist of approved dependencies.",
            reference: "https://cwe.mitre.org/data/definitions/829.html",
            detection: "Static",
            files: new[] { "package.json", "requirements.txt", "Cargo.toml", "setup.py" }
        ),

        // ============================================================
        // ⚠️ Rule: Missing SBOM (Software Bill of Materials)
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-DEP-005",
            title: "Missing Software Bill of Materials (SBOM)",
            desc: "Detects absence of a dependency manifest (SBOM), hindering visibility into third-party components.",
            category: SecurityCategory.Dependency,
            severity: SecuritySeverity.Low,
            risk: RiskLevel.Moderate,
            owasp: "A06:2021",
            cwe: "CWE-1104",
            vuln: VulnerabilityType.DependencyConfusion,
            tags: new[] { "sbom", "transparency", "dependency" },
            pattern: @"(?i)(package\.json|requirements\.txt|pom\.xml|Cargo\.toml)",
            surface: "Project Root",
            remediation: "Maintain an SBOM (CycloneDX, SPDX) and update it with each build to track components and transitive dependencies.",
            reference: "https://spdx.dev/",
            detection: "Review",
            files: new[] { ".json", ".xml", ".toml", ".txt" }
        )
    };

    public override string Key => "Dependency";
    public override string Name => "Dependency Vulnerability Rules";
    public override string Description =>
        "Analyzes third-party and transitive dependencies for CVEs, supply-chain risks, and version hygiene across ecosystems.";
    public override IReadOnlyCollection<SecurityRuleDefinition> Rules => _rules;
}
