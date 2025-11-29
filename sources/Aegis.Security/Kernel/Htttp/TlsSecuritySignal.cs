using Aegis.Security.Kernel.Http;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Kernel.Http
{
    public sealed record TlsSecuritySignal : SecuritySignal
    {
  
        public string Host { get; init; }
        public int Port { get; init; }

        public string Protocol { get; init; }
        public string? CipherSuite { get; init; }
        public int? KeySize { get; init; }
        public DateTime? ExpirationUtc { get; init; }
        public string? Error { get; init; }

        public TlsSecuritySignal(TlsScanResult result)
        {
            Host = result.Host;
            Port = result.Port;
            Protocol = result.Protocol;
            CipherSuite = result.CipherSuite;
            KeySize = result.KeySize;
            ExpirationUtc = result.ExpirationUtc;
            Error = result.Error;
        }
    }
}
