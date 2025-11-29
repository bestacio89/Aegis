using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Models;
using Aegis.Security.Kernel.Network.Signals;
using Aegis.Shared.Security.Enums;

namespace Aegis.Security.Evaluators.Network;

public sealed class NetworkSecurityEvaluator : ISecurityEvaluator
{
    public string Domain => "Network";

    private static readonly HashSet<int> CriticalExposedPorts = new()
    {
        22,   // SSH
        3389, // RDP
        3306, // MySQL
        5432, // PostgreSQL
        6379, // Redis
        27017 // MongoDB
    };

    public Task<SecurityEvaluationResult> EvaluateAsync(
        ProjectSecurityContext ctx,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default)
    {
        var findings = new List<SecurityFinding>();

        var nets = signals.OfType<PortSecuritySignal>().ToList();

        foreach (var port in nets)
        {
            // --- Critical exposed ports ---
            if (port.IsOpen && CriticalExposedPorts.Contains(port.Port))
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Critical,
                    $"Critical port {port.Port} is open on host {port.Host}.",
                    Remediation: "Close or restrict access via firewall or security group."
                ));
            }

            // --- Slow response ---
            if (port.IsOpen && port.LatencyMs > 500)
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Medium,
                    $"High network latency detected on port {port.Port} ({port.LatencyMs}ms)."
                ));
            }

            // --- Connection failures ---
            if (!string.IsNullOrWhiteSpace(port.Error))
            {
                findings.Add(new SecurityFinding(
                    SecuritySeverity.Low,
                    $"Port {port.Port} on {port.Host} produced an error: {port.Error}"
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

