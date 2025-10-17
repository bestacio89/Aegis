namespace Aegis.Shared.Models.Policies.Performance
{
    /// <summary>
    /// Governs code-level performance practices, concurrency, and resource usage across multiple languages.
    /// </summary>
    public class PerformancePolicy
    {
        public bool Enabled { get; set; } = true;

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
    }
}
