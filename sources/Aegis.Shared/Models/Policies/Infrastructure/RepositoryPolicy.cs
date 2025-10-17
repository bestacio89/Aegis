namespace Aegis.Shared.Models.Policies.Infrastructure
{
    /// <summary>
    /// Governs repository structure, governance, and hygiene requirements.
    /// Adaptive thresholds and enforcement behavior depend on <see cref="RepositoryType"/>.
    /// </summary>
    public class RepositoryPolicy
    {
        // ================================================================
        // 🧩 Core Repository Metadata
        // ================================================================
        public RepositoryType RepositoryType { get; set; } = RepositoryType.StandardService;

        public bool EnforceGovernanceFiles { get; set; } = true;
        public bool EnforceCiPresence { get; set; } = true;
        public bool RequireSingleRootSolution { get; set; } = true;

        // ================================================================
        // 📁 Required Files & Governance Structure
        // ================================================================
        public string[] RequiredFiles { get; set; } =
        [
            "README.md", "LICENSE", ".editorconfig", ".gitignore",
            "SECURITY.md", "CODEOWNERS", "CONTRIBUTING.md"
        ];

        /// <summary>
        /// Repository-level overrides for required files (applied dynamically by Aegis).
        /// Allows large platforms to define internal file exceptions.
        /// </summary>
        public Dictionary<string, bool>? RequiredFileOverrides { get; set; }

        /// <summary>
        /// Accepted CI/CD configuration paths and file patterns.
        /// </summary>
        public string[] CiPaths { get; set; } =
        [
            ".github/workflows", "azure-pipelines.yml", ".gitlab-ci.yml", ".circleci/config.yml"
        ];

        // ================================================================
        // ⚙️ Repository Quality & Hygiene
        // ================================================================
        public bool CheckLargeFiles { get; set; } = true;
        public bool CheckTodoDensity { get; set; } = true;
        public bool CheckProjectNaming { get; set; } = true;
        public bool RequireContributingGuide { get; set; } = true;
        public bool RequireVersionFile { get; set; } = true;
        public bool RequireDependencyLock { get; set; } = true;

        public int MaxTodoDensity { get; set; } = 5;
        public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024; // 20 MB

        // ================================================================
        // 🧱 Structural Complexity Constraints
        // ================================================================
        public int MaxProjectsPerRepo
        {
            get
            {
                return RepositoryType switch
                {
                    RepositoryType.Framework => 80,
                    RepositoryType.Platform => 40,
                    RepositoryType.StandardService => 12,
                    RepositoryType.Microservice => 8,
                    _ => 10
                };
            }
        }

        public int MaxNestedDepth { get; set; } = 5;
        public int MaxFileCount { get; set; } = 3000;

        // ================================================================
        // 🧾 Documentation & Versioning
        // ================================================================
        public bool RequireReadmeBadge { get; set; } = true;
        public bool RequireBuildBadge { get; set; } = true;
        public bool RequireTestCoverageBadge { get; set; } = true;
        public bool RequireLicenseConsistency { get; set; } = true;

        public string[] AllowedBadges { get; set; } =
        [
            "build", "coverage", "nuget", "npm", "pipeline", "release"
        ];
    }

    /// <summary>
    /// Defines expected repository archetypes, influencing threshold scaling.
    /// </summary>
    public enum RepositoryType
    {
        /// <summary> General purpose repository (default, small to medium projects). </summary>
        StandardService,

        /// <summary> Large frameworks or SDKs (e.g., Franz.Common, Archena, Aegis). </summary>
        Framework,

        /// <summary> Platform or suite (multi-module but cohesive). </summary>
        Platform,

        /// <summary> Compact or standalone service repository. </summary>
        Microservice
    }
}
