using Aegis.Security.Kernel.Cloud.Signals;
using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.Evaluators.Cloud;

public sealed class CloudSecurityEvaluator : ISecurityEvaluator
{
    public string Domain => "Cloud";

    public Task<SecurityEvaluationResult> EvaluateAsync(
        ProjectSecurityContext ctx,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default)
    {
        var findings = new List<SecurityFinding>();

        foreach (var bucket in signals.OfType<CloudBucketSecuritySignal>())
        {
            if (bucket.IsPublic)
                findings.Add(new(SecuritySeverity.High,
                    $"Bucket {bucket.BucketName} is public in {bucket.Provider}.",
                    Remediation: "Restrict bucket access to private or VPC-only."));

            if (!bucket.HasEncryption)
                findings.Add(new(SecuritySeverity.Critical,
                    $"Bucket {bucket.BucketName} has no encryption enabled.",
                    Remediation: "Enable server-side encryption."));
        }

        foreach (var iam in signals.OfType<CloudIamSecuritySignal>())
        {
            if (!iam.MfaEnabled)
                findings.Add(new(SecuritySeverity.High,
                    $"Principal {iam.PrincipalId} does not have MFA enabled."));

            if (iam.HasAdminRole)
                findings.Add(new(SecuritySeverity.Medium,
                    $"Principal {iam.PrincipalId} has admin roles attached."));
        }

        foreach (var net in signals.OfType<CloudNetworkSecuritySignal>())
        {
            if (net.IsOpenToWorld)
                findings.Add(new(SecuritySeverity.Critical,
                    $"Resource {net.ResourceId} exposes port {net.Port} to 0.0.0.0/0.",
                    Remediation: "Restrict security group to private CIDRs."));
        }

        return Task.FromResult(new SecurityEvaluationResult
        {
            Category = SecurityCategory.Cloud,
            Findings = findings
        });
    }
}
