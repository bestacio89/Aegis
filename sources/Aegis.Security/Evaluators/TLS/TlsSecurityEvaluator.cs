using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Aegis.Security.Kernel.Http;

namespace Aegis.Security.Evaluators.TLS;

public sealed class TlsSecurityEvaluator : ISecurityEvaluator
{
    public string Domain => "TLS";

    public Task<SecurityEvaluationResult> EvaluateAsync(
        ProjectSecurityContext ctx,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default)
    {
        var findings = new List<SecurityFinding>();

        foreach (var tls in signals.OfType<TlsSecuritySignal>())
        {
            // ==========================
            // 1. Protocol version checks
            // ==========================
            if (tls.Protocol is "Ssl2" or "Ssl3")
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Critical,
                    $"Host {tls.Host}:{tls.Port} uses deprecated and insecure SSL protocol ({tls.Protocol}).",
                    Remediation: "Disable SSLv2/SSLv3. Use TLS 1.2 or 1.3."
                ));
            }

            if (tls.Protocol is "Tls" or "Tls11" or "Tls10")
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.High,
                    $"Host {tls.Host}:{tls.Port} is using outdated TLS version ({tls.Protocol}).",
                    Remediation: "Upgrade to TLS 1.2 or, preferably, TLS 1.3."
                ));
            }

            // ==========================
            // 2. Weak Cipher Suite checks
            // ==========================
            if (tls.CipherSuite != null)
            {
                var cipher = tls.CipherSuite.ToUpperInvariant();

                if (cipher.Contains("NULL") ||
                    cipher.Contains("EXPORT") ||
                    cipher.Contains("RC4") ||
                    cipher.Contains("MD5") ||
                    cipher.Contains("DES") ||
                    cipher.Contains("3DES"))
                {
                    findings.Add(new SecurityFinding(
                        SecuritySeverity.Critical,
                        $"Host {tls.Host}:{tls.Port} negotiates an insecure cipher suite: {tls.CipherSuite}.",
                        Remediation: "Disable weak ciphers. Enforce AES-GCM or ChaCha20-Poly1305 suites."
                    ));
                }
            }

            // ==========================
            // 3. Certificate expiration
            // ==========================
            if (tls.ExpirationUtc is null)
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.High,
                    $"Host {tls.Host}:{tls.Port} does not provide a valid certificate."
                ));
            }
            else
            {
                var remaining = tls.ExpirationUtc.Value - DateTime.UtcNow;

                if (remaining <= TimeSpan.Zero)
                {
                    findings.Add(new SecurityFinding(
                        SecuritySeverity.Critical,
                        $"TLS certificate for {tls.Host}:{tls.Port} is EXPIRED!",
                        Remediation: "Renew and deploy a valid certificate immediately."
                    ));
                }
                else if (remaining <= TimeSpan.FromDays(30))
                {
                    findings.Add(new SecurityFinding(
                        SecuritySeverity.Medium,
                        $"TLS certificate for {tls.Host}:{tls.Port} expires in {remaining.Days} days.",
                        Remediation: "Renew certificate soon to avoid service interruption."
                    ));
                }
            }

            // ==========================
            // 4. Weak key size
            // ==========================
            if (tls.KeySize.HasValue)
            {
                if (tls.KeySize < 2048)
                {
                    findings.Add(new SecurityFinding(
                        SecuritySeverity.High,
                        $"Host {tls.Host}:{tls.Port} uses a weak certificate key size ({tls.KeySize} bits).",
                        Remediation: "Use RSA 2048+, ECDSA P-256+, or Ed25519 certificates."
                    ));
                }
            }

            // ==========================
            // 5. General TLS handshake errors
            // ==========================
            if (!string.IsNullOrWhiteSpace(tls.Error))
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Low,
                    $"TLS handshake error on {tls.Host}:{tls.Port}: {tls.Error}"
                ));
            }
        }

        return Task.FromResult(new SecurityEvaluationResult
        {
            Category = SecurityCategory.Network,
            Findings = findings
        });
    }
}
