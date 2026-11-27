using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Defines the fixed resource budget shared by the Java and Kotlin worker hosts.
/// These limits are admission controls; JVM flags are not an OS-level sandbox.
/// </summary>
internal static class JvmWorkerResourceBudget
{
    internal const int MaximumSourceFiles = 50_000;
    internal const int MaximumManifestBytes = 16 * 1024 * 1024;
    internal const int MaximumSourceRoots = 1_024;
    internal const int MaximumReferencedSourceRoots = 2_048;
    internal const int MaximumClasspathEntries = 8_192;
    internal const long MaximumSourceFileBytes = 16L * 1024 * 1024;
    internal const long MaximumAggregateSourceBytes = 512L * 1024 * 1024;

    internal static readonly string[] JvmArguments =
    [
        "-Xms64m", "-Xmx1024m", "-XX:MaxMetaspaceSize=384m",
        "-XX:MaxDirectMemorySize=128m", "-XX:ActiveProcessorCount=2",
        "-XX:+ExitOnOutOfMemoryError"
    ];

    internal static void ValidateRequest(SemanticAnalysisRequest request, string workerName)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCount(request.SourceFiles.Count, MaximumSourceFiles, "source files", workerName);
        ValidateCount(request.SourceRoots.Count, MaximumSourceRoots, "source roots", workerName);
        ValidateCount(request.SharedSourceRoots.Count, MaximumSourceRoots, "shared source roots", workerName);
        ValidateCount(request.TestSourceRoots.Count, MaximumSourceRoots, "test source roots", workerName);
        ValidateCount(request.ReferencedSourceRoots.Count, MaximumReferencedSourceRoots, "referenced source roots", workerName);
        ValidateCount(request.GeneratedSourceRoots.Count, MaximumReferencedSourceRoots, "generated source roots", workerName);
        ValidateCount(request.ClasspathEntries.Count, MaximumClasspathEntries, "classpath entries", workerName);

        long aggregateBytes = 0;
        foreach (var sourceFile in request.SourceFiles)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
            var file = new FileInfo(sourceFile);
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"{workerName} source input is missing or is a reparse point: {sourceFile}");
            var fileBytes = file.Length;
            if (fileBytes > MaximumSourceFileBytes)
                throw new InvalidOperationException($"{workerName} source file exceeds the {MaximumSourceFileBytes}-byte per-file budget: {sourceFile}");
            if (fileBytes > MaximumAggregateSourceBytes - aggregateBytes)
                throw new InvalidOperationException($"{workerName} source inputs exceed the {MaximumAggregateSourceBytes}-byte aggregate budget.");
            aggregateBytes += fileBytes;
        }
    }

    internal static void ValidateManifestBytes(int byteCount, string workerName)
    {
        if (byteCount is <= 0 or > MaximumManifestBytes)
            throw new InvalidOperationException($"{workerName} source manifest must contain between 1 and {MaximumManifestBytes} UTF-8 bytes.");
    }

    private static void ValidateCount(int count, int maximum, string description, string workerName)
    {
        if (count > maximum)
            throw new InvalidOperationException($"{workerName} request contains {count} {description}; the maximum is {maximum}.");
    }
}
