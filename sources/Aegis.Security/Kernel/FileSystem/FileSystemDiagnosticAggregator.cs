using Aegis.Shared.Security.Models;
using Aegis.Security.Kernel.FileSystem.Signals;

namespace Aegis.Security.Kernel.FileSystem;

public sealed class FileSystemDiagnosticsAggregator
{
    private readonly FileSystemScanner _scanner = new();

    public IEnumerable<SecuritySignal> ScanPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            yield return new FileSystemSecuritySignal(_scanner.Scan(path));
        }
    }
}
