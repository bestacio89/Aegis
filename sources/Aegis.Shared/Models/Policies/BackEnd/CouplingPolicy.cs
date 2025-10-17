namespace Aegis.Shared.Models.Policies.BackEnd
{
    /// <summary>
    /// Governs acceptable coupling levels and dependency graph depth
    /// across multi-language repositories.
    /// </summary>
    public class CouplingPolicy
    {
        /// <summary>
        /// Maximum imports/usings per file before warning.
        /// </summary>
        public int MaxImportsPerFile { get; set; } = 20;

        /// <summary>
        /// Maximum cross-module dependencies (within same language scope).
        /// </summary>
        public int MaxInternalModuleRefs { get; set; } = 15;

        /// <summary>
        /// Maximum external package references per project/module.
        /// </summary>
        public int MaxExternalDependencies { get; set; } = 30;

        /// <summary>
        /// Whether to recursively scan dependency manifests
        /// (package.json, requirements.txt, pom.xml, etc.)
        /// </summary>
        public bool DeepDependencyScan { get; set; } = true;

        /// <summary>
        /// Whether to warn when circular dependencies are detected.
        /// </summary>
        public bool DetectCircularDependencies { get; set; } = true;
    }
}
