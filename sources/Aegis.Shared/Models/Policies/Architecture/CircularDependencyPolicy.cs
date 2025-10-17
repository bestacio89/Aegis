namespace Aegis.Shared.Models.Policies.Architecture
{
    /// <summary>
    /// Governs detection of circular dependencies between modules, packages, or namespaces.
    /// </summary>
    public class CircularDependencyPolicy
    {
        /// <summary>Enables or disables circular dependency checks.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Maximum dependency chain depth before flagging a warning.</summary>
        public int MaxDependencyDepth { get; set; } = 10;

        /// <summary>Whether to treat circular dependencies as errors instead of warnings.</summary>
        public bool TreatAsError { get; set; } = true;

        /// <summary>Enable detection across different languages or project types.</summary>
        public bool CrossLanguageDetection { get; set; } = true;
    }
}
