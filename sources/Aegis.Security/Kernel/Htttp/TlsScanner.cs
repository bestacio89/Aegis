// Kernel/Http/TlsScanner.cs
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Aegis.Security.Kernel.Http;

public sealed class TlsScanner
{
    public async Task<TlsScanResult> ScanAsync(
        string host,
        int port = 443,
        int timeoutMs = 5000)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);

            await tcp.ConnectAsync(host, port, cts.Token);

            using var ssl = new SslStream(
                tcp.GetStream(),
                leaveInnerStreamOpen: false,
                (_, _, _, _) => true);

            await ssl.AuthenticateAsClientAsync(host);

            var cert = ssl.RemoteCertificate as X509Certificate2;

            return new TlsScanResult(
                Host: host,
                Port: port,
                Protocol: ssl.SslProtocol.ToString(),
                CipherSuite: ssl.NegotiatedCipherSuite.ToString(),
                CertificateIssuer: cert?.Issuer,
                CertificateSubject: cert?.Subject,
                ExpirationUtc: cert?.NotAfter.ToUniversalTime(),
                KeySize: cert?.PublicKey?.Key.KeySize
            );
        }
        catch (Exception ex)
        {
            return new TlsScanResult(
                Host: host,
                Port: port,
                Protocol: "Error",
                CipherSuite: null,
                CertificateIssuer: null,
                CertificateSubject: null,
                ExpirationUtc: null,
                KeySize: null,
                Error: ex.Message
            );
        }
    }
}

public sealed record TlsScanResult(
    string Host,
    int Port,
    string Protocol,
    string? CipherSuite,
    string? CertificateIssuer,
    string? CertificateSubject,
    DateTime? ExpirationUtc,
    int? KeySize,
    string? Error = null
);
