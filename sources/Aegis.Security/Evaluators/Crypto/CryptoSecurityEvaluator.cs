using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Aegis.Security.Kernel.Crypto.Signals;

namespace Aegis.Security.Evaluators.Crypto;

public sealed class CryptoSecurityEvaluator : ISecurityEvaluator
{
    public string Domain => "Crypto";

    public Task<SecurityEvaluationResult> EvaluateAsync(
        ProjectSecurityContext ctx,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default)
    {
        var findings = new List<SecurityFinding>();

        foreach (var sig in signals.OfType<CryptoSecuritySignal>())
        {
            if (sig.IsWeak)
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.High,
                    $"Weak cryptographic algorithm in use: {sig.Algorithm} (KeySize: {sig.KeySize}, Mode: {sig.Mode}).",
                    Remediation: "Replace with AES-256, SHA256/SHA512, RSA-2048+, or ECDSA P-256+."
                ));
            }

            if (!string.IsNullOrEmpty(sig.Error))
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Low,
                    $"Crypto scan error for {sig.Algorithm}: {sig.Error}"
                ));
            }
        }

        return Task.FromResult(new SecurityEvaluationResult
        {
            Category = SecurityCategory.Cryptography,
            Findings = findings
        });
    }
}
