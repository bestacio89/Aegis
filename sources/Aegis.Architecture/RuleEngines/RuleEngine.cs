
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

    private static readonly HashSet<string> RepositoryWideEvaluatorNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ArchitecturalEvaluator",
            "CircularDependencyEvaluator",
            "ConfigurationEvaluator",
            "DependencyGraphEvaluator",
            "PackageVersionEvaluator",
            "RepositoryEvaluator"
        };

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
        var semanticDiagnostics = CreateSemanticAnalysisDiagnostics(
            context.SemanticAnalysisRuns);
        var discoveryDiagnostics = context.DiscoveryDiagnostics.ToList();
        var scopes = DiscoverProjectScopes(
            projectPath,
            context,
            discoveryDiagnostics);

        // Rule applicability must see discovery evidence, not semantic-provider
        // diagnostics. Keep those channels distinct while preserving both in
        // the finalized report.
        context.DiscoveryDiagnostics = discoveryDiagnostics.ToList();
        var diagnostics = discoveryDiagnostics
            .Concat(semanticDiagnostics)
            .ToList();

        var executions = new List<(IEvaluator Evaluator, string Path, ProjectArchitectureContext Context)>();

        foreach (var evaluator in _evaluators)
        {
            // Evaluators that explicitly support every language and framework
            // inspect repository-level configuration/manifests once. All
            // technology-specific evaluators execute against detected project
            // boundaries so one project's framework cannot suppress another.
            var repositoryWide = RepositoryWideEvaluatorNames.Contains(evaluator.Name)
                || (evaluator.SupportedLanguages.Contains("*", StringComparer.OrdinalIgnoreCase)
                    && (evaluator.SupportedFrameworks.Contains("*", StringComparer.OrdinalIgnoreCase)
                        || evaluator.SupportedFrameworks.Contains("LanguageAgnostic", StringComparer.OrdinalIgnoreCase)));

            if (repositoryWide)
            {
                if (SupportsContext(evaluator, context))
                {
                    if (HasRequiredSemanticCapabilities(evaluator, context, projectPath, diagnostics))
                        executions.Add((evaluator, projectPath, context));
                }
                else
                {
                    LogUnsupported(evaluator, context);
                }

                continue;
            }

            var matchedScope = false;
            foreach (var scope in scopes)
            {
                if (!SupportsContext(evaluator, scope.Context))
                    continue;

                matchedScope = true;
                if (HasRequiredSemanticCapabilities(evaluator, scope.Context, scope.Path, diagnostics))
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

                    var evidencePath = result.Metadata?.GetValueOrDefault("FilePath")
                        ?? result.File
                        ?? result.Target;
                    if (pathFilter is not null && !pathFilter(evidencePath))
                        continue;

                    // Evaluators may complete their own analysis while encountering
                    // malformed or unreadable inputs. Preserve those diagnostics in
                    // the report; log output alone is not part of the audit evidence.
                    AppendEvaluatorDiagnostics(
                        diagnostics,
                        evaluator.Name,
                        result);



                    result.Source = evaluator.Name;
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
            "{Count} facts generated by {EvaluatorCount} evaluator executions across detected project scopes.",
            facts.Count,
            executions.Count);

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

        // Partial evaluator or discovery coverage cannot produce a complete assessment.
        if (diagnostics.Any(diagnostic => diagnostic.AffectsCoverage))
            report.Metrics.AssessmentComplete = false;

        var configurationDiagnosticCount = facts
            .Where(fact => fact is not null
                && string.Equals(
                    fact.Source,
                    "ConfigurationEvaluator",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    fact.Metadata?.GetValueOrDefault("EvaluationScope"),
                    "Project",
                    StringComparison.OrdinalIgnoreCase))
            .Sum(fact =>
            {
                var count = fact.Metrics.GetValueOrDefault("DiagnosticCount");
                return double.IsFinite(count)
                    ? (int)Math.Max(0, Math.Round(count))
                    : 0;
            });

        var incompleteFactCount = facts.Count(fact =>
        {
            if (fact is null)
                return false;

            var metadata = fact.Metadata;
            if (metadata is null)
                return false;

            if (string.Equals(
                    metadata.GetValueOrDefault("AnalysisComplete"),
                    "false",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var status = metadata.GetValueOrDefault("AnalysisStatus");
            return status is not null
                && (status.Equals("Incomplete", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("Partial", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("CompletedWithDiagnostics", StringComparison.OrdinalIgnoreCase));
        });

        var semanticDiagnosticCount = diagnostics.Count(diagnostic =>
            string.Equals(diagnostic.Source, "SemanticAnalysis", StringComparison.Ordinal));
        var discoveryDiagnosticCount = discoveryDiagnostics.Count;

        _logger.LogInformation(
            "Aegis scan completed. Violations={Violations}, Checks={Checks}, " +
            "RulesEvaluated={Evaluated}/{Applicable} supported applicable ({Coverage:0.0}%); " +
            "RegisteredRules={Registered}, RegistryEvaluationCoverage={RegistryCoverage:0.0}%; " +
            "AnalysisDiagnostics={AnalysisDiagnostics}, DiscoveryDiagnostics={DiscoveryDiagnostics}, " +
            "SemanticDiagnostics={SemanticDiagnostics}, ConfigurationDiagnostics={ConfigurationDiagnostics}, " +
            "IncompleteFacts={IncompleteFacts}",
            report.TotalViolations,
            outcome.Stats.ChecksPerformed,
            outcome.Stats.RulesEvaluated,
            outcome.Stats.RulesApplicable,
            outcome.Stats.EvaluationCoverage,
            outcome.Stats.RulesRegistered,
            outcome.Stats.RegisteredRuleEvaluationCoverage,
            diagnostics.Count,
            discoveryDiagnosticCount,
            semanticDiagnosticCount,
            configurationDiagnosticCount,
            incompleteFactCount);

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

    private static List<AnalysisDiagnostic> CreateSemanticAnalysisDiagnostics(
        IEnumerable<SemanticAnalysisRun> runs)
    {
        var diagnostics = new List<AnalysisDiagnostic>();

        foreach (var run in runs)
        {
            foreach (var diagnostic in run.Diagnostics)
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = diagnostic.Code,
                    Component = string.IsNullOrWhiteSpace(diagnostic.Component)
                        ? run.EngineId
                        : diagnostic.Component,
                    Source = "SemanticAnalysis",
                    Language = run.Language,
                    EngineId = run.EngineId,
                    RunId = run.RunId,
                    ProjectPath = run.ProjectPath,
                    Severity = diagnostic.Severity,
                    Line = diagnostic.Line,
                    Column = diagnostic.Column,
                    Message = diagnostic.Message,
                    Target = diagnostic.Target,
                    ExceptionType = diagnostic.ExceptionType,
                    AffectsCoverage = diagnostic.AffectsCoverage,
                    Timestamp = diagnostic.Timestamp
                });
            }

            if (!run.IsComplete && !run.Diagnostics.Any(diagnostic => diagnostic.AffectsCoverage))
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "SemanticAnalysisIncomplete",
                    Component = run.EngineId,
                    Source = "SemanticAnalysis",
                    Language = run.Language,
                    EngineId = run.EngineId,
                    RunId = run.RunId,
                    ProjectPath = run.ProjectPath,
                    Message = $"Semantic analysis for {run.Language} ended with status '{run.Status}' without a coverage-affecting diagnostic.",
                    Target = run.ProjectPath,
                    AffectsCoverage = true,
                    Timestamp = run.CompletedAtUtc ?? DateTimeOffset.UtcNow
                });
            }
        }

        return diagnostics;
    }

    private static void AppendEvaluatorDiagnostics(
        ICollection<AnalysisDiagnostic> diagnostics,
        string evaluatorName,
        ArchitectureEvaluatorResult result)
    {
        var metadata = result.Metadata;
        if (metadata is null)
            return;

        var status = metadata.GetValueOrDefault("AnalysisStatus");
        var detailText = metadata.GetValueOrDefault("Diagnostics");
        var messages = string.IsNullOrWhiteSpace(detailText)
            ? new List<string>()
            : detailText.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        var serializedDetailCount = messages.Count;

        var incompleteReason = metadata.GetValueOrDefault("IncompleteReason");
        if (!string.IsNullOrWhiteSpace(incompleteReason))
            messages.Add(incompleteReason);

        var unreadableFilePaths = metadata.GetValueOrDefault("UnreadableFilePaths");
        if (!string.IsNullOrWhiteSpace(unreadableFilePaths))
            messages.Add($"Unreadable source paths (up to 20):{Environment.NewLine}{unreadableFilePaths}");

        messages = messages
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var reportedCount = result.Metrics.TryGetValue("DiagnosticCount", out var count)
            && double.IsFinite(count)
            && count > 0
            ? (int)Math.Ceiling(count)
            : 0;

        var hasPartialStatus = string.Equals(
                status,
                "CompletedWithDiagnostics",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Partial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Incomplete", StringComparison.OrdinalIgnoreCase);

        if (!hasPartialStatus && reportedCount == 0 && messages.Count == 0)
            return;

        foreach (var message in messages)
        {
            diagnostics.Add(new AnalysisDiagnostic
            {
                Code = "EvaluatorReportedDiagnostic",
                Component = evaluatorName,
                Message = message,
                Target = result.Target,
                AffectsCoverage = true
            });
        }

        // Some evaluators cap serialized detail to keep reports bounded. Preserve
        // the declared count as evidence so truncation cannot make coverage look clean.
        var omittedCount = Math.Max(0, reportedCount - serializedDetailCount);
        if (omittedCount > 0 || (hasPartialStatus && messages.Count == 0 && reportedCount == 0))
        {
            diagnostics.Add(new AnalysisDiagnostic
            {
                Code = omittedCount > 0
                    ? "EvaluatorDiagnosticDetailsTruncated"
                    : "EvaluatorPartialAnalysis",
                Component = evaluatorName,
                Message = omittedCount > 0
                    ? $"The evaluator reported {reportedCount} diagnostics but serialized only {messages.Count} detail message(s); {omittedCount} diagnostic detail(s) are unavailable."
                    : $"The evaluator reported analysis status '{status}' but did not provide diagnostic details or a diagnostic count.",
                Target = result.Target,
                AffectsCoverage = true
            });
        }
    }

    private sealed record AnalysisScope(string Path, ProjectArchitectureContext Context);

    private IReadOnlyList<AnalysisScope> DiscoverProjectScopes(
        string projectPath,
        ProjectArchitectureContext rootContext,
        ICollection<AnalysisDiagnostic> diagnostics)
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
                var moduleContext = ProjectArchitectureContextDetector.Detect(path);

                foreach (var diagnostic in moduleContext.DiscoveryDiagnostics)
                {
                    if (!diagnostics.Any(existing =>
                            string.Equals(existing.Code, diagnostic.Code, StringComparison.Ordinal)
                            && string.Equals(existing.Target, diagnostic.Target, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(existing.Message, diagnostic.Message, StringComparison.Ordinal)))
                    {
                        diagnostics.Add(diagnostic);
                    }
                }

                // Keep technology applicability local to this project, while
                // retaining the repository-wide graph and module inventory.
                // Otherwise project-scoped evaluators lose cross-project
                // references and misclassify layer relationships as absent.
                moduleContext.Modules = rootContext.Modules;
                moduleContext.SemanticAnalysisRuns = rootContext.SemanticAnalysisRuns;
                moduleContext.Layers = rootContext.Layers;
                moduleContext.Dependencies = rootContext.Dependencies;
                moduleContext.Boundaries = rootContext.Boundaries;
                moduleContext.PatternsDetected = rootContext.PatternsDetected;

                scopes.Add(new AnalysisScope(path, moduleContext));
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "ProjectScopeDiscoveryFailed",
                    Component = nameof(ProjectArchitectureContextDetector),
                    Message = "The module context could not be detected; module-specific evaluators were not executed.",
                    Target = path,
                    ExceptionType = exception.GetType().FullName,
                    AffectsCoverage = true
                });

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
        if (!SupportsLanguage(evaluator, context))
        {
            return false;
        }

        // Framework identity is module-owned. The context's primary framework
        // remains descriptive and must not hide additional frameworks in mixed modules.
        var frameworks = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Framework))
            frameworks.Add(context.Framework);
        if (context.DetectedFrameworks is not null)
            frameworks.AddRange(context.DetectedFrameworks.Where(value => !string.IsNullOrWhiteSpace(value)));

        var exactModules = context.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .Where(module => PathsEqual(module.Path, context.RootPath))
            .ToArray();
        IEnumerable<ProjectModuleContext> relevantModules = exactModules.Length > 0 ? exactModules : context.Modules;
        foreach (var module in relevantModules)
            frameworks.AddRange(module.Frameworks.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (frameworks.Count == 0)
            return SupportsFramework(evaluator, context.Framework);

        return frameworks.Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(framework => SupportsFramework(evaluator, framework));
    }

    private static bool SupportsLanguage(
        IEvaluator evaluator,
        ProjectArchitectureContext context)
    {
        if (evaluator.SupportedLanguages.Contains("*", StringComparer.OrdinalIgnoreCase))
            return true;

        return GetLanguagesForScope(context, context.RootPath).Any(language =>
            evaluator.SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase));
    }

    private bool HasRequiredSemanticCapabilities(
        IEvaluator evaluator,
        ProjectArchitectureContext context,
        string evaluationPath,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var required = evaluator.RequiredSemanticCapabilities
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (required.Length == 0)
            return true;

        var matchingModules = context.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .Where(module => PathsEqual(module.Path, evaluationPath)
                || (!context.Modules.Any(candidate => PathsEqual(candidate.Path, evaluationPath))
                    && IsSameOrDescendantPath(module.Path, evaluationPath)))
            .ToArray();

        var scopes = matchingModules.Length == 0
            ? new[] { (Path: evaluationPath, Languages: GetLanguagesForScope(context, evaluationPath)) }
            : matchingModules.Select(module => (
                Path: module.Path,
                Languages: (IReadOnlyCollection<string>)(module.Languages.Count == 0
                    ? GetLanguagesForScope(context, evaluationPath).ToArray()
                    : module.Languages.ToArray()))).ToArray();

        var missingByScope = new List<string>();
        foreach (var scope in scopes)
        {
            var languages = scope.Languages
                .Where(language => evaluator.SupportedLanguages.Contains("*", StringComparer.OrdinalIgnoreCase)
                    || evaluator.SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (languages.Length == 0)
            {
                missingByScope.Add($"Unknown ({Path.GetFileName(scope.Path)}): {string.Join(", ", required)}");
                continue;
            }

            foreach (var language in languages)
            {
                var available = context.SemanticAnalysisRuns
                    .Where(run => PathsEqual(run.ProjectPath, scope.Path))
                    .Where(run => run.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
                    .Where(run => run.Status is Aegis.Shared.Architecture.Enums.SemanticAnalysisStatus.Completed
                        or Aegis.Shared.Architecture.Enums.SemanticAnalysisStatus.PartiallyCompleted)
                    .SelectMany(run => run.Capabilities)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var missing = required.Where(capability => !available.Contains(capability)).ToArray();
                if (missing.Length > 0)
                    missingByScope.Add($"{language} ({Path.GetFileName(scope.Path)}): {string.Join(", ", missing)}");
            }
        }

        if (missingByScope.Count == 0)
            return true;

        diagnostics.Add(new AnalysisDiagnostic
        {
            Code = "EvaluatorSemanticCapabilitiesUnavailable",
            Component = evaluator.Name,
            Message = $"Evaluator '{evaluator.Name}' was not executed because required semantic evidence is unavailable: {string.Join("; ", missingByScope)}.",
            Target = evaluationPath,
            AffectsCoverage = true
        });

        _logger.LogWarning(
            "Evaluator {Evaluator} was not executed for {ProjectPath}; required semantic capabilities are unavailable: {MissingCapabilities}.",
            evaluator.Name,
            evaluationPath,
            string.Join("; ", missingByScope));

        return false;
    }

    private static IReadOnlyCollection<string> GetLanguagesForScope(
        ProjectArchitectureContext context,
        string scopePath)
    {
        var module = context.Modules.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.Path)
            && PathsEqual(candidate.Path, scopePath));

        var languages = module?.Languages
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .ToList() ?? [];

        if (!string.IsNullOrWhiteSpace(context.Language)
            && !context.Language.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            && !languages.Contains(context.Language, StringComparer.OrdinalIgnoreCase))
        {
            languages.Add(context.Language);
        }

        return languages;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }

    private static bool IsSameOrDescendantPath(string candidate, string parent)
    {
        var normalizedCandidate = Path.GetFullPath(candidate);
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
                normalizedCandidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalizedParent,
                comparison)
            || normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, comparison);
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
