using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.RuleEngine;



public sealed class SecurityEvaluatorEngine : ISecurityEvaluatorEngine
{
    private readonly IEnumerable<ISecurityEvaluator> _evaluators;
    private readonly ILogger<SecurityEvaluatorEngine> _logger;

    public SecurityEvaluatorEngine(IEnumerable<ISecurityEvaluator> evaluators, ILogger<SecurityEvaluatorEngine> logger)
    {
        _evaluators = evaluators;
        _logger = logger;
    }

    public async Task<IEnumerable<SecurityEvaluationResult>> EvaluateAllAsync(
        ProjectSecurityContext ctx,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default)
    {
        var results = new List<SecurityEvaluationResult>();

        foreach (var evaluator in _evaluators)
        {
            try
            {
                _logger.LogDebug("Executing evaluator {Domain}", evaluator.Domain);

                var result = await evaluator.EvaluateAsync(ctx, signals, ct);
                results.Add(result);

                _logger.LogInformation(
                    "Evaluator {Domain} completed: {Severity} ({Score:F2})",
                    evaluator.Domain,
                    result.MaxSeverity,
                    result.AverageScore);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Evaluator {Domain} failed", evaluator.Domain);
            }
        }

        return results;
    }
}

