using Aegis.Shared.Security.Models;

public interface ISecurityEvaluatorEngine
{
    Task<IEnumerable<SecurityEvaluationResult>> EvaluateAllAsync(
        ProjectSecurityContext context,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default);
}
