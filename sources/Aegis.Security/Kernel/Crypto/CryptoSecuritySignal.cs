using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.Crypto.Signals;

public sealed record CryptoSecuritySignal : SecuritySignal
{
    public string Algorithm { get; init; } = string.Empty;
    public int? KeySize { get; init; }
    public string? Mode { get; init; }
    public bool IsWeak { get; init; }
    public string? Error { get; init; }

    public CryptoSecuritySignal(CryptoScanResult result)
    {
        Algorithm = result.Algorithm;
        KeySize = result.KeySize;
        Mode = result.Mode;
        IsWeak = result.IsWeak;
        Error = result.Error;
    }
}
