using Aegis.Core.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Contracts;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Utilities;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Core.Architecture.Evaluators;

/// <summary>
/// 🧠 Base abstraction for all Aegis Evaluators.
/// Evaluators analyze source code and produce structured metrics or facts (EvaluatorResults)
/// that will later be interpreted by the RuleEngine.
/// </summary>
public abstract class BaseArchitectureEvaluator : IEvaluator, IScopedDependency
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
    /// Indicates whether this evaluator is currently enabled (policy driven).
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Weight factor applied to this evaluator’s contribution to scoring.
    /// </summary>
    public double WeightFactor { get; set; } = 1.0;

    /// <summary>
    /// Optional context (language, framework, project metadata).
    /// </summary>
    protected ProjectArchitectureContext? Context { get; private set; }

    protected BaseArchitectureEvaluator(ILogger logger) => _logger = logger;

    // ===============================================================
    // 🧩 Policy Application
    // ===============================================================
    /// <summary>
    /// Applies policy configuration to this evaluator.
    /// Derived evaluators can override this to read their specific policy block.
    /// </summary>
    public virtual void ApplyPolicy(AegisArchitecturePolicy policy)
    {
        // Example of policy-driven toggling
        switch (Name)
        {
            case "PerformanceEvaluator":
                IsEnabled = policy.Performance.Enabled;
                WeightFactor = 1.2;
                break;

            case "MaintainabilityEvaluator":
                IsEnabled = policy.Maintainability.MinMaintainabilityIndex > 0;
                WeightFactor = 1.0;
                break;

            case "SecurityEvaluator":
                IsEnabled = policy.Security.Enabled;
                WeightFactor = 1.5;
                break;

            default:
                IsEnabled = true;
                WeightFactor = 1.0;
                break;
        }

        _logger.LogDebug("⚙️ Evaluator '{Name}' → Enabled={Enabled}, Weight={Weight}",
            Name, IsEnabled, WeightFactor);
    }

    // ===============================================================
    // 🚀 Evaluation Pipeline
    // ===============================================================
    public async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateAsync(
        string projectPath,
        ProjectArchitectureContext context,
        CancellationToken token = default)
    {
        Context = context;

        if (!IsEnabled)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                $"⏭️ Evaluator disabled by policy. Skipping {Name}.");
            return Enumerable.Empty<ArchitectureEvaluatorResult>();
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
            $"Starting evaluation for {context.Language}/{context.Framework}.");

        try
        {
            var results = await EvaluateCoreAsync(projectPath, token);
            var count = results.Count();

            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                $"Completed evaluation with {count} result(s).");

            // Optionally scale impact scores using WeightFactor
            foreach (var r in results)
            {
                r.WeightFactor = WeightFactor;
            }

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
    protected abstract Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath, CancellationToken token);

    /// <summary>
    /// Determines whether the given path belongs to an excluded directory.
    /// </summary>
    protected static bool IsExcludedDir(string path) => PathUtils.IsExcludedDir(path);

    /// <summary>
    /// Optional helper for backward compatibility:
    /// converts evaluator outputs to a RuleResult form (temporary migration).
    /// </summary>
    protected static IEnumerable<ArchitectureRuleresult> ConvertToRuleResults(
        IEnumerable<ArchitectureEvaluatorResult> evalResults, string ruleId)
        => evalResults.Select(e =>
        {
            var category = ArchitectureRuleCategory.General;
            if (!string.IsNullOrWhiteSpace(e.Category) &&
                Enum.TryParse<ArchitectureRuleCategory>(e.Category, true, out var parsed))
            {
                category = parsed;
            }

            return new ArchitectureRuleresult
            {
                RuleId = ruleId,
                Message = $"{e.Source}: {e.Target} ({string.Join(", ", e.Metrics.Select(m => $"{m.Key}={m.Value:0.##}"))})",
                Category = category,
                Severity = ArchitectureRuleSeverity.Info
            };
        });
}
