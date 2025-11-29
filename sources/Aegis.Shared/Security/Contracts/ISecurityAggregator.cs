using Aegis.Shared.Security.Models;

namespace Aegis.Shared.Security.Contracts;

/// <summary>
/// Aggregates domain-level evaluation results into a complete security report.
/// </summary>
public interface ISecurityAggregator
{
    /// <summary>
    /// Combines multiple domain evaluation results into a consolidated report.
    /// </summary>
    Task<AegisSecurityReport> AggregateAsync(
        IEnumerable<SecurityEvaluationResult> evaluations,
        CancellationToken cancellationToken = default);
}
