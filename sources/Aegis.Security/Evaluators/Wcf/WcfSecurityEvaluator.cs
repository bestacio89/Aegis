using Aegis.Security.Kernel.WCF;
using Aegis.Security.Kernel.WCF.Signals;
using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Evaluators.WCF;

public sealed class WcfSecurityEvaluator : ISecurityEvaluator
{
    public string Domain => "WCF";

    public Task<SecurityEvaluationResult> EvaluateAsync(
        ProjectSecurityContext ctx,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default)
    {
        var findings = new List<SecurityFinding>();

        foreach (var s in signals.OfType<WcfSecuritySignal>())
        {
            // 1. No transport security
            if (!s.IsHttps && s.SecurityMode == "None")
            {
                findings.Add(new(SecuritySeverity.Critical,
                    $"{s.ServiceName} exposes a WCF endpoint without HTTPS or message security.",
                    Remediation: "Enable Transport or Message security."));
            }

            // 2. Anonymous client access
            if (s.AllowsAnonymous)
            {
                findings.Add(new(SecuritySeverity.High,
                    $"{s.ServiceName} allows anonymous clients.",
                    Remediation: "Require userName, Windows, or certificate authentication."));
            }

            // 3. Weak algorithm suite
            if (s.AlgorithmSuite is "Basic128" or "TripleDes" or "Basic128Rsa15")
            {
                findings.Add(new(SecuritySeverity.High,
                    $"{s.ServiceName} uses a weak algorithm suite ({s.AlgorithmSuite}).",
                    Remediation: "Upgrade to Basic256Sha256 or stronger."));
            }

            // 4. Metadata exposure
            if (s.MetadataExposed)
            {
                findings.Add(new(SecuritySeverity.Low,
                    $"{s.ServiceName} exposes service metadata.",
                    Remediation: "Disable metadata publishing in production environments."));
            }

            // 5. Unprotected binding types
            if (s.Binding.Contains("basicHttpBinding") && !s.IsHttps)
            {
                findings.Add(new(SecuritySeverity.Critical,
                    $"{s.ServiceName} uses basicHttpBinding without HTTPS (passwords transmitted in clear-text).",
                    Remediation: "Use basicHttpsBinding or wsHttpBinding with message security."));
            }
        }

        return Task.FromResult(new SecurityEvaluationResult
        {
            Category = SecurityCategory.Application,
            Findings = findings
        });
    }
}
