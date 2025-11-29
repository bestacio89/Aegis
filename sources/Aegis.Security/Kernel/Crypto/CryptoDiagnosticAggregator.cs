using Aegis.Shared.Security.Models;
using Aegis.Security.Kernel.Crypto.Signals;

namespace Aegis.Security.Kernel.Crypto;

public sealed class CryptoDiagnosticsAggregator
{
    private readonly CryptoScanner _scanner = new();

    public IEnumerable<SecuritySignal> Collect()
    {
        foreach (var result in _scanner.Scan())
        {
            yield return new CryptoSecuritySignal(result);
        }
    }
}
