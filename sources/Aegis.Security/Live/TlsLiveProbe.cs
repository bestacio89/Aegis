using Aegis.Security.Evaluators.TLS;
using Aegis.Security.Kernel.Http;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Live;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Live;

/// <summary>
/// Passive TLS observation: completes one handshake and reports what the server actually
/// presented. The judgement rules are the existing <see cref="TlsSecurityEvaluator"/> (single
/// source of truth for what counts as weak).
///
/// Limit to keep in mind: this reports the protocol the server NEGOTIATED with this client, which is
/// the strongest it will agree to. It does not prove that older protocols are disabled; that needs
/// separate forced-protocol handshakes (a later, Light-intensity probe).
/// </summary>
public sealed class TlsLiveProbe : ILiveProbe
{
    private const string Control = "ISO/IEC 27001:2022 A.8.24 (use of cryptography)";

    private readonly TlsScanner _scanner = new();
    private readonly TlsSecurityEvaluator _rules = new();
    private readonly TimeProvider _clock;

    public TlsLiveProbe(TimeProvider clock) => _clock = clock;

    public string Id => "tls-handshake";

    public string Name => "TLS handshake observation";

    public ProbeIntensity Intensity => ProbeIntensity.Passive;

    public async Task<IReadOnlyList<LiveProbeResult>> ExecuteAsync(
        LiveTarget target,
        IProbeLog log,
        CancellationToken ct)
    {
        var results = new List<LiveProbeResult>();

        log.Record(Id, target.Display, "tls-handshake", "initiating");

        // The scanner has its own connect timeout, but its handshake call takes no token.
        // WaitAsync lets the pipeline's per-probe timeout and operator cancellation win.
        var scan = await _scanner.ScanAsync(target.Host, target.Port).WaitAsync(ct);

        if (!string.IsNullOrWhiteSpace(scan.Error))
        {
            log.Record(Id, target.Display, "tls-handshake", "failed", scan.Error);

            results.Add(new LiveProbeResult(
                Id, Name, target.Display, _clock.GetUtcNow(),
                SecuritySeverity.Info,
                $"No TLS handshake completed on {target.Display}",
                $"The connection or handshake failed: {scan.Error}. The port may be closed, filtered, or not speaking TLS.",
                EvidenceConfidence.Informational,
                null,
                null));

            return results;
        }

        log.Record(Id, target.Display, "tls-handshake", "completed",
            $"protocol={scan.Protocol}; cipher={scan.CipherSuite}");

        var evidence = DescribeHandshake(scan);

        // Always record what was observed, so a clean result is evidence too, not an empty list.
        results.Add(new LiveProbeResult(
            Id, Name, target.Display, _clock.GetUtcNow(),
            SecuritySeverity.Info,
            $"Observed {scan.Protocol} on {target.Display}",
            evidence,
            EvidenceConfidence.Confirmed,
            Control,
            null));

        // Judge it with the shared TLS rules.
        var context = new ProjectSecurityContext
        {
            ProjectPath = target.Display,
            RepositoryRoot = target.Display,
            Environment = "Live"
        };

        var evaluation = await _rules.EvaluateAsync(context, new SecuritySignal[] { new TlsSecuritySignal(scan) }, ct);

        foreach (var finding in evaluation.Findings)
        {
            results.Add(new LiveProbeResult(
                Id, Name, target.Display, _clock.GetUtcNow(),
                finding.SeverityLevel,
                finding.Message,
                evidence,
                EvidenceConfidence.Confirmed,
                Control,
                finding.Remediation));
        }

        return results;
    }

    private static string DescribeHandshake(TlsScanResult scan)
    {
        var expiry = scan.ExpirationUtc is { } e ? e.ToString("yyyy-MM-dd") + " UTC" : "unknown";

        return $"Negotiated protocol: {scan.Protocol}; cipher suite: {scan.CipherSuite ?? "unknown"}; " +
               $"certificate subject: {scan.CertificateSubject ?? "unknown"}; issuer: {scan.CertificateIssuer ?? "unknown"}; " +
               $"expires: {expiry}; key size: {(scan.KeySize?.ToString() ?? "unknown")} bits.";
    }
}