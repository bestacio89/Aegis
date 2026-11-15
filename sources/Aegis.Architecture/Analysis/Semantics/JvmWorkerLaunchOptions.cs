namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Describes how a supervised JVM worker is launched and which JSONL protocol
/// limits apply to its process channel.
/// </summary>
internal sealed record JvmWorkerLaunchOptions(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    int ProtocolVersion,
    TimeSpan StartupTimeout,
    int MaxFrameBytes,
    int MaxResponseBytes,
    string WorkerName);
