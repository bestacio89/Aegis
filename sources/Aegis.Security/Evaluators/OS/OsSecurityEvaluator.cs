using Aegis.Security.Kernel.OS.Signals;
using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed class OsSecurityEvaluator : ISecurityEvaluator
{
    public string Domain => "OS";

    public Task<SecurityEvaluationResult> EvaluateAsync(
        ProjectSecurityContext ctx,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default)
    {
        var findings = new List<SecurityFinding>();

        // --- OS INFO ---
        var osInfo = signals.OfType<OsInfoSecuritySignal>().FirstOrDefault();
        if (osInfo != null && osInfo.OSVersion.Contains("Windows 7"))
        {
            findings.Add(new SecurityFinding(
                SecuritySeverity.High,
                "Operating system is end-of-life (Windows 7 detected)."
            ));
        }

        // --- HARDENING ---
        foreach (var hard in signals.OfType<OsHardeningSecuritySignal>())
        {
            if (!hard.FirewallEnabled)
                findings.Add(new SecurityFinding(SecuritySeverity.High,
                    "Firewall is disabled."));

            if (hard.Smb1Enabled)
                findings.Add(new SecurityFinding(SecuritySeverity.Critical,
                    "SMBv1 is enabled — unsafe legacy protocol."));

            if (hard.IsGuestAccountEnabled)
                findings.Add(new SecurityFinding(SecuritySeverity.Medium,
                    "Guest account is enabled."));
        }

        // --- ENV VARIABLES ---
        foreach (var env in signals.OfType<EnvironmentVariableSecuritySignal>())
        {
            if (env.Key.Contains("SECRET", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Critical,
                    $"Environment variable '{env.Key}' may contain secrets."
                ));
            }
        }

        // --- PROCESSES ---
        foreach (var proc in signals.OfType<ProcessSecuritySignal>())
        {
            if (proc.Name.Equals("nc.exe", StringComparison.OrdinalIgnoreCase) ||
                (proc.Name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) && proc.MemoryMb > 500))
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.High,
                    $"Suspicious high-memory process detected: {proc.Name}"
                ));
            }
        }

        return Task.FromResult(new SecurityEvaluationResult
        {
            Category = SecurityCategory.Infrastructure,
            Findings = findings
        });
    }
}