using Aegis.Shared.Security.Models;

namespace Aegis.Security.Pipeline;

public interface IAegisSecurityPipeline
{
    Task<(AegisSecurityReport Report, SecurityComplianceResult Compliance)> RunAsync(
        ProjectSecurityContext context,
        CancellationToken ct = default);
}
