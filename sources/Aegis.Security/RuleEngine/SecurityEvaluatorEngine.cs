using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
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
        ProbeMode mode,
        CancellationToken ct = default)
    {
        var results = new List<SecurityEvaluationResult>();
        var signalList = signals.ToList();

        var selected = _evaluators
            .Where(e => SecurityModeCatalog.ModeOfDomain(e.Domain) == mode)
            .ToList();

        if (selected.Count == 0)
        {
            _logger.LogWarning("No {Mode} evaluators are registered; nothing will be assessed.", mode);
            return results;
        }

        foreach (var evaluator in selected)
        {
            try
            {
                _logger.LogDebug("Executing {Mode} evaluator {Domain}", mode, evaluator.Domain);

                var result = await evaluator.EvaluateAsync(ctx, signalList, ct);
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