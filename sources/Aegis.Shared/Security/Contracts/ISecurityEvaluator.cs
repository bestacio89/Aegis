using Aegis.Shared.Security.Models;

namespace Aegis.Shared.Security.Contracts;

/// <summary>
/// Domain-specific risk evaluator that transforms raw probe signals
/// into structured evaluation results.
/// </summary>
public interface ISecurityEvaluator
{
    /// <summary>
    /// Domain handled by this evaluator (Network, IaC, Secrets, Governance, etc.)
    /// </summary>
    string Domain { get; }

    /// <summary>
    /// Computes an evaluation result for this domain based on
    /// the current project context and probe signals.
    /// </summary>
    Task<SecurityEvaluationResult> EvaluateAsync(
        ProjectSecurityContext context,
        IEnumerable<SecuritySignal> signals,
        CancellationToken cancellationToken = default);
}
