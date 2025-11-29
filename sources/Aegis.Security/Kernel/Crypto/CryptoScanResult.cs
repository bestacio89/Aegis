namespace Aegis.Security.Kernel.Crypto;

public sealed record CryptoScanResult(
    string Algorithm,
    int? KeySize,
    string? Mode,
    bool IsWeak,
    string? Error
);
