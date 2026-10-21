
using Aegis.Architecture.Aggregation;
using Aegis.Architecture.Analysis;
using Aegis.Architecture.Scoring;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.RuleEngines;

/// <summary>
/// Orchestrates evaluator execution, rule interpretation, weighting,
/// aggregation, and report enrichment.
/// </summary>
public sealed class RuleEngine
{
    private readonly IReadOnlyCollection<IEvaluator> _evaluators;
    private readonly RuleEngineCore _core;
    private readonly RuleWeightingEngine _weighting;
    private readonly CrossEvaluatorAggregator _aggregator;
    private readonly ILogger<RuleEngine> _logger;

    public RuleEngine(
        IEnumerable<IEvaluator> evaluators,
        RuleEngineCore core,
        RuleWeightingEngine weighting,
        CrossEvaluatorAggregator aggregator,
        ILogger<RuleEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(evaluators);
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(weighting);
        ArgumentNullException.ThrowIfNull(aggregator);
        ArgumentNullException.ThrowIfNull(logger);

        _evaluators = evaluators.ToArray();
        _core = core;
        _weighting = weighting;
        _aggregator = aggregator;
        _logger = logger;

        foreach (var evaluator in _evaluators)
        {
            _logger.LogInformation(
                "Registered evaluator: {Evaluator}",
                evaluator.Name);
        }

        _logger.LogInformation(
            "Aegis RuleEngine initialized with {Count} evaluators.",
            _evaluators.Count);
    }

    public void ApplyPolicy(AegisArchitecturePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        _core.ApplyPolicy(policy);
        _weighting.ApplyPolicy(policy);
        _aggregator.ApplyPolicy(policy);

        _logger.LogInformation(
            "Aegis policy applied. Version={Version}. Evaluators={Count}",
            policy.Version ?? "Unknown",
            _evaluators.Count);
    }

    private const double EvaluatorsStart = 10;
    private const double EvaluatorsEnd = 80;

    /// <summary>
    /// Executes the analysis pipeline.
    /// Evaluator failures are recorded without discarding successful results
    /// from other evaluators.
    /// </summary>
    public async Task<AegisArchitectureReport> RunAsync(
        string projectPath,
        ProjectArchitectureContext context,
        CancellationToken token = default,
        Func<string, bool>? pathFilter = null,
        IProgress<AnalysisProgressInfo>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogInformation(
            "Starting Aegis scan for {Project}",
            context.ProjectName);

        var facts = new List<ArchitectureEvaluatorResult>();
        var diagnostics = new List<AnalysisDiagnostic>();
        var scopes = DiscoverProjectScopes(projectPath, context);
        var executions = new List<(IEvaluator Evaluator, string Path, ProjectArchitectureContext Context)>();

        foreach (var evaluator in _evaluators)
        {
            // Evaluators that explicitly support every language and framework
            // inspect repository-level configuration/manifests once. All
            // technology-specific evaluators execute against detected project
            // boundaries so one project's framework cannot suppress another.
            var repositoryWide = evaluator.SupportedLanguages.Contains("*", StringComparer.OrdinalIgnoreCase)
                && (evaluator.SupportedFrameworks.Contains("*", StringComparer.OrdinalIgnoreCase)
                    || evaluator.SupportedFrameworks.Contains("LanguageAgnostic", StringComparer.OrdinalIgnoreCase));

            if (repositoryWide)
            {
                if (SupportsContext(evaluator, context))
                    executions.Add((evaluator, projectPath, context));
                else
                    LogUnsupported(evaluator, context);
                continue;
            }

            var matchedScope = false;
            foreach (var scope in scopes)
            {
                if (!SupportsContext(evaluator, scope.Context))
                    continue;

                matchedScope = true;
                executions.Add((evaluator, scope.Path, scope.Context));
            }

            if (!matchedScope)
                LogUnsupported(evaluator, context);
        }

        for (var i = 0; i < executions.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var (evaluator, evaluationPath, evaluationContext) = executions[i];
            var percent = executions.Count == 0
                ? EvaluatorsEnd
                : EvaluatorsStart + (EvaluatorsEnd - EvaluatorsStart) * i / executions.Count;

            Report(progress, percent, "Evaluating",
                $"{evaluator.Name} [{evaluationContext.ProjectName}] ({i + 1}/{executions.Count})");

            _logger.LogInformation(
                "Running evaluator: {Evaluator} on project {Project} ({Index}/{Total})",
                evaluator.Name,
                evaluationContext.ProjectName,
                i + 1,
                executions.Count);

            try
            {
                var results = await evaluator.EvaluateAsync(
                    evaluationPath,
                    evaluationContext,
                    token);

                if (results is null)
                {
                    diagnostics.Add(new AnalysisDiagnostic
                    {
                        Code = "EvaluatorReturnedNull",
                        Component = evaluator.Name,
                        Message = "The evaluator returned a null result collection.",
                        AffectsCoverage = true
                    });

                    _logger.LogWarning(
                        "Evaluator {Evaluator} returned a null result collection for {Project}.",
                        evaluator.Name,
                        evaluationContext.ProjectName);
                    continue;
                }

                foreach (var result in results)
                {
                    token.ThrowIfCancellationRequested();

                    if (result is null)
                    {
                        diagnostics.Add(new AnalysisDiagnostic
                        {
                            Code = "EvaluatorReturnedNullFact",
                            Component = evaluator.Name,
                            Message = "The evaluator returned a null fact.",
                            AffectsCoverage = true
                        });
                        continue;
                    }

                    if (pathFilter is not null && !pathFilter(result.Target))
                        continue;

                    result.ProjectName ??= evaluationContext.ProjectName;
                    facts.Add(result);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "EvaluatorFailed",
                    Component = evaluator.Name,
                    Message = $"Evaluator '{evaluator.Name}' failed during analysis of '{evaluationContext.ProjectName}'.",
                    ExceptionType = ex.GetType().FullName,
                    AffectsCoverage = true
                });

                _logger.LogError(
                    ex,
                    "Evaluator {Evaluator} failed for project {Project}. The report will contain a coverage diagnostic.",
                    evaluator.Name,
                    evaluationContext.ProjectName);
            }
        }

        _logger.LogInformation(
            "{Count} facts generated by {EvaluatorCount} applicable evaluators.",
            facts.Count,
            applicable.Count);

        Report(
            progress,
            EvaluatorsEnd,
            "Interpreting",
            $"{facts.Count:N0} facts against the rule set");

        var outcome = _core.EvaluateDetailed(context, facts);
        var ruleResults = outcome.Results.ToList();

        _logger.LogInformation(
            "{Count} rule violations generated.",
            ruleResults.Count);

        Report(
            progress,
            88,
            "Weighting",
            $"{ruleResults.Count:N0} violations");

        // Weight findings only. Passing evaluations are not findings.
        var weightedResults = _weighting
            .ApplyWeights(ruleResults)
            .ToList();

        Report(
            progress,
            90,
            "Aggregating",
            "Domains and compliance");

        var report = _aggregator.Aggregate(
            weightedResults,
            facts,
            context,
            outcome.Stats);

        // Persist every scoped evaluation, including passing evaluations.
        foreach (var evaluation in outcome.Evaluations)
        {
            report.Evaluations.Add(evaluation);
        }

        // Preserve evaluator failures in the report instead of relying
        // on log output, which consumers cannot reliably inspect.
        foreach (var diagnostic in diagnostics)
        {
            report.Diagnostics.Add(diagnostic);
        }

        _logger.LogInformation(
            "Aegis scan completed. Violations={Violations}, Checks={Checks}, " +
            "RulesEvaluated={Evaluated}/{Registered}, Diagnostics={Diagnostics}",
            report.TotalViolations,
            outcome.Stats.ChecksPerformed,
            outcome.Stats.RulesEvaluated,
            outcome.Stats.RulesRegistered,
            diagnostics.Count);

        if (outcome.Stats.ChecksPerformed == 0)
        {
            _logger.LogWarning(
                "No rule comparisons were completed against the {Registered} registered rules. " +
                "Compliance and health figures may not be meaningful.",
                outcome.Stats.RulesRegistered);
        }

        if (diagnostics.Any(diagnostic => diagnostic.AffectsCoverage))
        {
            _logger.LogWarning(
                "The report was generated with incomplete evaluator coverage. " +
                "Coverage-affecting diagnostics={Count}.",
                diagnostics.Count(diagnostic => diagnostic.AffectsCoverage));
        }

        Report(
            progress,
            92,
            "Analysis complete",
            null);

        return report;
    }

    private sealed record AnalysisScope(string Path, ProjectArchitectureContext Context);

    private IReadOnlyList<AnalysisScope> DiscoverProjectScopes(
        string projectPath,
        ProjectArchitectureContext rootContext)
    {
        var root = Path.GetFullPath(projectPath);
        var modules = rootContext.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .Select(module => Path.GetFullPath(module.Path))
            .Where(path => Directory.Exists(path)
                && !string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ToList();

        // A module nested inside another discovered module is not scanned twice.
        var roots = new List<string>();
        foreach (var path in modules)
        {
            if (roots.Any(parent => path.StartsWith(
                    parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)))
                continue;
            roots.Add(path);
        }

        if (roots.Count == 0)
            return [new AnalysisScope(root, rootContext)];

        var scopes = new List<AnalysisScope>();
        foreach (var path in roots)
        {
            try
            {
                scopes.Add(new AnalysisScope(
                    path,
                    ProjectArchitectureContextDetector.Detect(path)));
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                _logger.LogWarning(
                    exception,
                    "Could not detect project context for module {Path}; module-specific evaluators will not run.",
                    path);
            }
        }

        // Preserve root-level evaluation if module detection was incomplete.
        return scopes.Count == 0
            ? [new AnalysisScope(root, rootContext)]
            : scopes;
    }

    private void LogUnsupported(
        IEvaluator evaluator,
        ProjectArchitectureContext context)
    {
        _logger.LogDebug(
            "Evaluator {Evaluator} did not match any detected project scope. Root context: {Language}/{Framework}; detected frameworks: {Frameworks}",
            evaluator.Name,
            context.Language,
            context.Framework,
            string.Join(", ", context.DetectedFrameworks));
    }

    private static void Report(
        IProgress<AnalysisProgressInfo>? progress,
        double percent,
        string stage,
        string? detail) =>
        progress?.Report(
            new AnalysisProgressInfo(percent, stage, detail));

    private static bool SupportsContext(
        IEvaluator evaluator,
        ProjectArchitectureContext context)
    {
        if (!SupportsLanguage(evaluator, context.Language))
        {
            return false;
        }

        // A repository context may contain several valid framework signals.
        // The primary framework is descriptive, not an exclusive allow-list.
        var frameworks = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Framework))
            frameworks.Add(context.Framework);
        if (context.DetectedFrameworks is not null)
            frameworks.AddRange(context.DetectedFrameworks.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (frameworks.Count == 0)
            return SupportsFramework(evaluator, context.Framework);

        return frameworks.Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(framework => SupportsFramework(evaluator, framework));
    }

    private static bool SupportsLanguage(
        IEvaluator evaluator,
        string language)
    {
        return evaluator.SupportedLanguages.Contains(
                   "*",
                   StringComparer.OrdinalIgnoreCase)
               ||
               evaluator.SupportedLanguages.Contains(
                   language,
                   StringComparer.OrdinalIgnoreCase);
    }

    private static bool SupportsFramework(
        IEvaluator evaluator,
        string? framework)
    {
        if (evaluator.SupportedFrameworks.Contains(
                "*",
                StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (evaluator.SupportedFrameworks.Contains(
                "LanguageAgnostic",
                StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(framework))
        {
            return true;
        }

        foreach (var supported in evaluator.SupportedFrameworks)
        {
            if (framework.Equals(
                    supported,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsDotNetFamily(framework) &&
                IsDotNetFamily(supported))
            {
                return true;
            }

            if (framework.Contains(
                    "ASP.NET",
                    StringComparison.OrdinalIgnoreCase) &&
                supported.Contains(
                    "ASP.NET",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDotNetFamily(string framework)
    {
        return framework.Contains(
                   ".NET",
                   StringComparison.OrdinalIgnoreCase)
               ||
               framework.Contains(
                   "EF Core",
                   StringComparison.OrdinalIgnoreCase)
               ||
               framework.Contains(
                   "ASP.NET",
                   StringComparison.OrdinalIgnoreCase);
    }
}
