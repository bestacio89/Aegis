using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules.Sets;

/// <summary>
/// Infrastructure-as-Code security rules — detects critical misconfigurations in cloud and deployment templates
/// (Terraform, Bicep, ARM, Kubernetes, CloudFormation).
/// </summary>
public sealed class IaCRuleSet : SecurityRuleSet
{
    private static readonly SecurityRuleDefinition[] _rules =
    {
        // ============================================================
        // 🌍 Rule: Unrestricted Ingress Rule
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-IAC-001",
            title: "Unrestricted Ingress Rule",
            desc: "Detects public inbound rules exposing sensitive or administrative ports (22, 3389, 5432, etc.).",
            category: SecurityCategory.IaC,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A05:2021",
            cwe: "CWE-284",
            vuln: VulnerabilityType.UnrestrictedIngressRules,
            tags: new[] { "iac", "network", "exposure", "ingress" },
            pattern: @"0\.0\.0\.0/0",
            surface: "Network Edge",
            remediation: "Restrict ingress sources to trusted CIDRs, VPNs, or security groups. Never allow 0.0.0.0/0.",
            reference: "https://docs.aws.amazon.com/vpc/latest/userguide/VPC_SecurityGroups.html",
            detection: "IaC-Parser",
            files: new[] { ".tf", ".bicep", ".json", ".yml", ".yaml" }
        ),

        // ============================================================
        // 🔑 Rule: Plaintext Secret in IaC
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-IAC-002",
            title: "Plaintext Secret in IaC",
            desc: "Detects hardcoded secrets, tokens, or passwords within IaC configuration files.",
            category: SecurityCategory.IaC,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A07:2021",
            cwe: "CWE-798",
            vuln: VulnerabilityType.SecretExposure,
            tags: new[] { "secret", "credential", "password", "iac" },
            pattern: @"(?i)(password\s*=\s*\"".+\""|secret\s*=\s*\"".+\""|api[_-]?key\s*=\s*\"".+\"")",
            surface: "IaC Config / Environment Variable",
            remediation: "Use environment variables or secret managers (AWS Secrets Manager, Azure Key Vault, HashiCorp Vault).",
            reference: "https://owasp.org/Top10/A07_2021-Identification_and_Authentication_Failures/",
            detection: "Static",
            files: new[] { ".tf", ".bicep", ".yml", ".yaml", ".json" }
        ),

        // ============================================================
        // ☁️ Rule: Publicly Accessible Storage Bucket
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-IAC-003",
            title: "Publicly Accessible Storage Bucket",
            desc: "Detects storage buckets with public access enabled (AWS S3, Azure Blob, GCP Storage).",
            category: SecurityCategory.IaC,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Severe,
            owasp: "A05:2021",
            cwe: "CWE-284",
            vuln: VulnerabilityType.PublicBucketAccess,
            tags: new[] { "storage", "bucket", "public", "acl" },
            pattern: @"(?i)(public\s*=\s*true|acl\s*=\s*\""?public-read\""?|allUsers)",
            surface: "Cloud Storage Configuration",
            remediation: "Set access to private or authenticated users only; disable public ACLs and enable block public access policies.",
            reference: "https://docs.aws.amazon.com/AmazonS3/latest/userguide/access-control-block-public-access.html",
            detection: "IaC-Parser",
            files: new[] { ".tf", ".bicep", ".json", ".yml", ".yaml" }
        ),

        // ============================================================
        // 🔐 Rule: Missing Encryption Configuration
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-IAC-004",
            title: "Missing Encryption Configuration",
            desc: "Detects resources lacking encryption at rest (e.g., databases, disks, or buckets).",
            category: SecurityCategory.IaC,
            severity: SecuritySeverity.High,
            risk: RiskLevel.Moderate,
            owasp: "A02:2021",
            cwe: "CWE-311",
            vuln: VulnerabilityType.MissingEncryptionAtRest,
            tags: new[] { "encryption", "kms", "data", "iac" },
            pattern: @"(?i)(encryption\s*=\s*false|kms_key_id\s*=\s*null|use_encryption\s*=\s*false)",
            surface: "Storage / Database Resources",
            remediation: "Enable encryption using cloud-native KMS or specify managed encryption keys for all data stores.",
            reference: "https://owasp.org/Top10/A02_2021-Cryptographic_Failures/",
            detection: "Static",
            files: new[] { ".tf", ".bicep", ".json", ".yml", ".yaml" }
        ),

        // ============================================================
        // 👥 Rule: Excessive IAM Permissions
        // ============================================================
        SecurityRuleSetFactory.Create(
            id: "SEC-IAC-005",
            title: "Excessive IAM Permissions",
            desc: "Detects overly permissive IAM roles or policies (e.g., '*', 'AdministratorAccess').",
            category: SecurityCategory.IaC,
            severity: SecuritySeverity.Critical,
            risk: RiskLevel.Severe,
            owasp: "A05:2021",
            cwe: "CWE-732",
            vuln: VulnerabilityType.ExcessiveIAMPermissions,
            tags: new[] { "iam", "policy", "permissions", "role" },
            pattern: @"(?i)(\""?Action\""?\s*:\s*\[\s*\""\*\""\s*\]|\""?PolicyName\""?\s*:\s*\""?AdministratorAccess\""?)",
            surface: "IAM Policy / Role Definition",
            remediation: "Apply the principle of least privilege — define only the actions required for the service or role.",
            reference: "https://docs.aws.amazon.com/IAM/latest/UserGuide/best-practices.html",
            detection: "IaC-Parser",
            files: new[] { ".tf", ".bicep", ".json", ".yml" }
        )
    };

    public override string Key => "IaC";
    public override string Name => "Infrastructure-as-Code Rules";
    public override string Description =>
        "Detects misconfigurations in Terraform, Bicep, Kubernetes, and CloudFormation templates, covering network, IAM, storage, and encryption risks.";
    public override IReadOnlyCollection<SecurityRuleDefinition> Rules => _rules;
}
