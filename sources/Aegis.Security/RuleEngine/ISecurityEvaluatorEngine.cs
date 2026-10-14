using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;

namespace Aegis.Security.RuleEngine;

public interface ISecurityEvaluatorEngine
{
    /// <summary>
    /// Runs every registered evaluator whose domain belongs to <paramref name="mode"/>
    /// (see <see cref="SecurityModeCatalog"/>), so a static scan never reports a live domain
    /// as "assessed" without having observed anything.
    /// </summary>
    Task<IEnumerable<SecurityEvaluationResult>> EvaluateAllAsync(
        ProjectSecurityContext context,
        IEnumerable<SecuritySignal> signals,
        ProbeMode mode,
        CancellationToken ct = default);
}