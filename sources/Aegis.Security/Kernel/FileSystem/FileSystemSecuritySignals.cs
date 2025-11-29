using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.FileSystem.Signals;

public sealed record FileSystemSecuritySignal : SecuritySignal
{
    public string FilePath { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public bool IsSensitive { get; init; }
    public bool IsWorldReadable { get; init; }
    public bool IsWorldWritable { get; init; }
    public bool ContainsSecrets { get; init; }
    public long SizeBytes { get; init; }
    public string? Error { get; init; }

    public FileSystemSecuritySignal(FileSystemScanResult result)
    {
        FilePath = result.Path;
        Exists = result.Exists;
        IsSensitive = result.IsSensitive;
        IsWorldReadable = result.IsWorldReadable;
        IsWorldWritable = result.IsWorldWritable;
        ContainsSecrets = result.ContainsSecrets;
        SizeBytes = result.SizeBytes;
        Error = result.Error;
    }
}
