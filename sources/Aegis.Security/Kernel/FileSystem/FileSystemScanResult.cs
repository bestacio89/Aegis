namespace Aegis.Security.Kernel.FileSystem;

public sealed record FileSystemScanResult(
    string Path,
    bool Exists,
    bool IsSensitive,
    bool IsWorldReadable,
    bool IsWorldWritable,
    bool ContainsSecrets,
    long SizeBytes,
    string? Error = null
);
