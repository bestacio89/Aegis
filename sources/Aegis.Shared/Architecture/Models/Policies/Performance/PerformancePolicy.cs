using Aegis.Shared.Architecture.Models;

namespace Aegis.Shared.Architecture.Models.Policies.Performance
{
    /// <summary>
    /// Governs both micro-level code performance rules and macro-level
    /// scoring sensitivity for resilience and health evaluation.
    /// </summary>
    public class PerformancePolicy
    {
        public bool Enabled { get; set; } = true;

        // =========================================================
        // ⚙️ Micro-level (code analysis) rules
        // =========================================================

        // 🔁 Loop analysis
        public int MaxNestedLoopDepth { get; set; } = 3;
        public int MaxLoopBodyLength { get; set; } = 100;

        // ⚙️ Concurrency / async rules
        public bool RequireAsyncForIOMethods { get; set; } = true;
        public bool DisallowBlockingCalls { get; set; } = true;
        public bool CheckThreadSleepUsage { get; set; } = true;

        // 🧮 Memory / string handling
        public bool DetectStringConcatenationInLoops { get; set; } = true;
        public bool DetectLargeCollectionsInitialization { get; set; } = true;
        public int LargeCollectionThreshold { get; set; } = 1000;

        // 🧵 Threading misuse
        public bool WarnThreadCreationInHotPaths { get; set; } = true;
        public bool DetectSynchronousWaits { get; set; } = true; // .Result / .Wait()

        // 🔍 Profiling advice
        public bool SuggestAsyncStreams { get; set; } = true;

        // =========================================================
        // 🧭 Macro-level (evaluation & aggregation tuning)
        // =========================================================

        /// <summary>
        /// Adjusts how much the performance of each rule affects the overall
        /// <see cref="GlobalArchitectureMetrics.ProjectHealthIndex"/> (default = 1.0).
        /// </summary>
        public double GlobalHealthWeight { get; set; } = 1.0;

        /// <summary>
        /// Controls how strongly variance in domain scores affects
        /// <see cref="GlobalArchitectureMetrics.ResilienceIndex"/>.
        /// Higher = more sensitive to uneven performance across modules.
        /// </summary>
        public double ResilienceSensitivity { get; set; } = 1.0;

        /// <summary>
        /// Optional coefficient used by the weighting engine to scale
        /// performance-related rule severities (e.g., slow loops, blocking IO).
        /// </summary>
        public double PerformanceWeightScale { get; set; } = 1.0;

        /// <summary>
        /// Defines a minimum threshold below which rule impact scores
        /// are considered negligible for global aggregation.
        /// </summary>
        public double MinimumPerformanceImpactThreshold { get; set; } = 0.05;

        /// <summary>
        /// Optional coefficient used when blending performance with
        /// maintainability to calculate project-level health.
        /// </summary>
        public double MaintainabilityBlendFactor { get; set; } = 0.5;
    }
}
