using Aegis.Shared.Security.Models;

namespace Aegis.Shared.Security.Contracts;

/// <summary>
/// Applies security policies and compliance thresholds to aggregated reports.
/// </summary>
public interface ISecurityRuleEngine
{
    Task<SecurityComplianceResult> EvaluateComplianceAsync(
        AegisSecurityReport report,
        CancellationToken cancellationToken = default);
}
