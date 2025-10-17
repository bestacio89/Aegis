using Aegis.Core.Diagnostics;
using Aegis.Shared.Contracts;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Aegis.Shared.Models.Rules;
using Aegis.Shared.Utilities;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Core.Evaluators;

/// <summary>
/// Base abstraction for all Aegis Evaluators.
/// Evaluators analyze source code and produce structured metrics or facts (EvaluatorResults)
/// that will later be interpreted by the RuleEngine.
/// </summary>
public abstract class BaseEvaluator : IEvaluator, IScopedDependency
{
    protected readonly ILogger _logger;

    /// <summary>
    /// Unique evaluator name, e.g. "ComplexityEvaluator" or "ApiConsistencyEvaluator".
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Supported languages for this evaluator (defaults to all).
    /// </summary>
    public virtual string[] SupportedLanguages { get; } = ["*"];

    /// <summary>
    /// Supported frameworks for this evaluator (defaults to all).
    /// </summary>
    public virtual string[] SupportedFrameworks { get; } = ["*"];

    /// <summary>
    /// Optional context (language, framework, project metadata).
    /// </summary>
    protected ProjectContext? Context { get; private set; }

    protected BaseEvaluator(ILogger logger) => _logger = logger;

    /// <summary>
    /// Executes the evaluator on a given project path and context.
    /// Returns structured EvaluatorResults (not rule evaluations).
    /// </summary>
    public async Task<IEnumerable<EvaluatorResult>> EvaluateAsync(
        string projectPath,
        ProjectContext context,
        CancellationToken token = default)
    {
        Context = context;

        AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
            $"Starting evaluation for {context.Language}/{context.Framework}.");

        try
        {
            var results = await EvaluateCoreAsync(projectPath, token);

            var count = results.Count();
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                $"Completed evaluation with {count} result(s).");

            return results;
        }
        catch (Exception ex)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Error, "Evaluation failed.", ex);

#if DEBUG
            throw;
#else
            return Enumerable.Empty<EvaluatorResult>();
#endif
        }
    }

    /// <summary>
    /// Core logic implemented by derived evaluators.
    /// Should perform the actual analysis and produce EvaluatorResults.
    /// </summary>
    protected abstract Task<IEnumerable<EvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token);

    /// <summary>
    /// Determines whether the given path belongs to an excluded directory.
    /// </summary>
    protected static bool IsExcludedDir(string path) => PathUtils.IsExcludedDir(path);

    /// <summary>
    /// Optional helper for backward compatibility:
    /// converts evaluator outputs to a RuleResult form (temporary migration).
    /// </summary>
    protected static IEnumerable<RuleResult> ConvertToRuleResults(IEnumerable<EvaluatorResult> evalResults, string ruleId)
        => evalResults.Select(e => new RuleResult
        {
            RuleId = ruleId,
            Message = $"{e.Source}: {e.Target} ({string.Join(", ", e.Metrics.Select(m => $"{m.Key}={m.Value:0.##}"))})",
            Category = e.Category ?? "General",
            Severity = RuleSeverity.Info
        });
}
