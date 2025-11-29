using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Aegis.Security.Kernel.FileSystem.Signals;

namespace Aegis.Security.Evaluators.FileSystem;

public sealed class FileSystemSecurityEvaluator : ISecurityEvaluator
{
    public string Domain => "FileSystem";

    public Task<SecurityEvaluationResult> EvaluateAsync(
        ProjectSecurityContext ctx,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default)
    {
        var findings = new List<SecurityFinding>();

        foreach (var sig in signals.OfType<FileSystemSecuritySignal>())
        {
            if (!sig.Exists)
                continue;

            // Sensitive file exposed
            if (sig.IsSensitive && sig.IsWorldReadable)
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Critical,
                    $"Sensitive file '{sig.FilePath}' is world-readable.",
                    Remediation: "Restrict file permissions: only application/service accounts should have access."
                ));
            }

            // Writable by everyone
            if (sig.IsWorldWritable)
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.High,
                    $"File '{sig.FilePath}' is world-writable. This is a privilege-escalation risk.",
                    Remediation: "Remove write permissions for 'Everyone' group."
                ));
            }

            // Secrets inside files
            if (sig.ContainsSecrets)
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Critical,
                    $"Secrets detected inside file '{sig.FilePath}'.",
                    Remediation: "Move secrets to a secure vault (Azure KeyVault, AWS SecretsManager)."
                ));
            }

            // Unexpectedly large files
            if (sig.SizeBytes > 100_000_000) // > 100MB
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Low,
                    $"File '{sig.FilePath}' is unusually large ({sig.SizeBytes} bytes)."
                ));
            }

            // File read errors
            if (!string.IsNullOrWhiteSpace(sig.Error))
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Low,
                    $"Error scanning file '{sig.FilePath}': {sig.Error}"
                ));
            }
        }

        return Task.FromResult(new SecurityEvaluationResult
        {
            Category = SecurityCategory.FileSystem,
            Findings = findings
        });
    }
}
