
using Microsoft.Extensions.Logging;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Architecture.RuleEngines;

/// <summary>
/// Converts evaluator facts into rule evaluations and architecture findings.
///
/// Every valid rule/metric comparison is recorded, regardless of whether
/// the rule passes or fails. Findings contain violations only.
///
/// Project-summary facts are matched only to project-scoped rules; file/entity
/// facts are matched only to file-scoped rules. Explicit scope metadata takes
/// precedence over the legacy ArchitectureSummary category fallback.
/// Missing metrics, inapplicable rules, invalid thresholds, and non-finite
/// measurements remain distinct evaluation outcomes.
/// </summary>
public sealed class RuleEngineCore
{
    private const string EvaluationScopeKey = "EvaluationScope";
    private const string ProjectScope = "Project";
    private const string FilePathKey = "FilePath";
    private const string ArchitectureSummaryCategory = "ArchitectureSummary";

    private readonly ILogger<RuleEngineCore> _logger;
    private readonly IReadOnlyList<ArchitectureRuleDefinition> _rules;

    private AegisArchitecturePolicy _policy;

    public RuleEngineCore(
        ILogger<RuleEngineCore> logger,
        AegisArchitecturePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(policy);

        _logger = logger;
        _policy = policy;

        _rules = ArchitectureRuleRegistry.All
            .Where(rule => rule.Enabled)
            .ToList();
    }

    public void ApplyPolicy(AegisArchitecturePolicy newPolicy)
    {
        ArgumentNullException.ThrowIfNull(newPolicy);

        _policy = newPolicy;

        _logger.LogInformation(
            "Aegis rule policy applied: {Name} (v{Version})",
            newPolicy.Name ?? "Unnamed",
            newPolicy.Version ?? "1.0");
    }

    public IEnumerable<ArchitectureRuleresult> Evaluate(
        ProjectArchitectureContext context,
        IEnumerable<ArchitectureEvaluatorResult> facts) =>
        EvaluateDetailed(context, facts).Results;

    public RuleEvaluationOutcome EvaluateDetailed(
        ProjectArchitectureContext context,
        IEnumerable<ArchitectureEvaluatorResult> facts)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(facts);

        var factList = facts.ToList();
        var evaluations = new List<RuleEvaluationResult>();
        var results = new List<ArchitectureRuleresult>();
     

        var evaluatedRuleIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var notApplicableReasons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var unsupportedRuleReasons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var applicableRules = new List<ArchitectureRuleDefinition>();

        foreach (var rule in _rules)
        {
            if (!ArchitectureRuleMetricContracts.TryGetApplicability(
                    rule,
                    context,
                    out var applicabilityReason))
            {
                notApplicableReasons[rule.Id] = applicabilityReason;
                continue;
            }

            var producerDisposition =
                ArchitectureRuleMetricContracts.GetProducerContractDisposition(rule.Id);

            if (producerDisposition is not null)
            {
                unsupportedRuleReasons[rule.Id] = producerDisposition;
                continue;
            }

            applicableRules.Add(rule);
        }

        var rulesByDomain =
            new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);

        var buckets =
            new Dictionary<
                (string Domain, ArchitectureRuleSeverity Severity, string Category),
                int>();

        var layerCache =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        var checksPassed = 0;
        var checksViolated = 0;
        var projectSummaryFacts = 0;
        var skippedNonFiniteMetrics = 0;
        var skippedInvalidThresholds = 0;
        var factsWithoutMatchingScopedMetrics = 0;
        var factsWithMetricsAtWrongScope = 0;
        var factsWithKnownMetricsWithoutEligibleRule = 0;

        foreach (var fact in factList)
        {
            if (fact is null)
            {
                _logger.LogWarning(
                    "Null evaluator fact encountered; skipping it.");

                continue;
            }

            var isProjectFact = IsProjectSummary(fact);

            if (isProjectFact)
            {
                projectSummaryFacts++;
            }

            var filePath =
                fact.Metadata?.GetValueOrDefault(FilePathKey)
                ?? fact.Target;

            var domain = isProjectFact
                ? "Project"
                : ResolveLayerCached(
                    filePath,
                    context,
                    layerCache);

            // Metric inventory and rule eligibility are separate questions.
            // A metric declared by an unsupported or currently inapplicable
            // rule is not an unknown metric-production gap.
            var matchedAnyRegisteredMetric = _rules.Any(rule =>
                ArchitectureRuleMetricContracts.TryResolveMetric(
                    rule,
                    fact.Metrics,
                    out _,
                    out _));

            var matchedAnyDeclaredMetric = false;
            var matchedAnyScopedMetric = false;

            foreach (var rule in applicableRules)
            {
                var metricExists = ArchitectureRuleMetricContracts.TryResolveMetric(
                    rule,
                    fact.Metrics,
                    out var metricKey,
                    out var metricValue);

                // A metric key alone is insufficient: first establish that
                // this rule is allowed to consume evidence from this fact.
                if (!ArchitectureRuleMetricContracts.TryGetFactApplicability(
                        rule,
                        fact,
                        out _))
                {
                    continue;
                }

                if (!metricExists)
                {
                    continue;
                }

                matchedAnyDeclaredMetric = true;

                // Scope is part of the rule contract. Track metric evidence at
                // an incompatible scope for diagnostics, but never evaluate it.
                if ((rule.Scope == RuleEvaluationScope.Project) != isProjectFact)
                {
                    continue;
                }

                matchedAnyScopedMetric = true;

                // A non-finite observation cannot produce a meaningful
                // comparison and must not count as a completed check.
                if (!double.IsFinite(metricValue))
                {
                    skippedNonFiniteMetrics++;

                    _logger.LogWarning(
                        "Skipping non-finite metric {MetricKey} from " +
                        "{Evaluator} for rule {RuleId}, target {Target}. " +
                        "Value={Value}",
                        metricKey,
                        fact.Source,
                        rule.Id,
                        fact.Target,
                        metricValue);

                    continue;
                }

                var threshold = ResolveThreshold(rule);

                if (!double.IsFinite(threshold))
                {
                    skippedInvalidThresholds++;

                    _logger.LogWarning(
                        "Rule {RuleId} has a non-finite threshold {Threshold}; " +
                        "the check was skipped.",
                        rule.Id,
                        threshold);

                    continue;
                }

                var isViolation = IsViolation(
                    rule.Operator,
                    metricValue,
                    threshold);

                var evaluatedAt = DateTimeOffset.UtcNow;

                // Record both outcomes. Results below remain violations only.
                evaluations.Add(new RuleEvaluationResult
                {
                    RuleId = rule.Id,
                    RuleName = rule.Name,
                    MetricKey = metricKey,
                    Status = isViolation
                        ? RuleEvaluationStatus.Violated
                        : RuleEvaluationStatus.Passed,
                    Scope = rule.Scope.ToString(),
                    Target = isProjectFact
                        ? fact.ProjectName ?? fact.Target
                        : fact.Target,
                    Domain = domain,
                    ObservedValue = metricValue,
                    Threshold = threshold,
                    ComparisonOperator = rule.Operator.ToString(),
                    ReasonCode = isViolation
                        ? "RuleViolated"
                        : "RulePassed",
                    Reason = isViolation
                        ? $"Metric '{metricKey}' violated the configured threshold."
                        : $"Metric '{metricKey}' satisfied the configured threshold.",
                    Evaluator = fact.Source,
                    EvaluatedAt = evaluatedAt
                });

                evaluatedRuleIds.Add(rule.Id);

                if (isViolation)
                {
                    checksViolated++;
                }
                else
                {
                    checksPassed++;
                }

                if (!rulesByDomain.TryGetValue(domain, out var domainRules))
                {
                    domainRules = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                    rulesByDomain[domain] = domainRules;
                }

                domainRules.Add(rule.Id);

                var bucketKey = (domain, rule.Severity, rule.Category);

                buckets[bucketKey] =
                    buckets.GetValueOrDefault(bucketKey) + 1;

                if (!isViolation)
                {
                    continue;
                }

                results.Add(CreateRuleResult(
                    rule,
                    fact,
                    context,
                    domain,
                    metricKey,
                    metricValue,
                    threshold,
                    evaluatedAt));
            }

            if (fact.Metrics.Count > 0 && !matchedAnyRegisteredMetric)
            {
                factsWithoutMatchingScopedMetrics++;
            }
            else if (matchedAnyRegisteredMetric && !matchedAnyDeclaredMetric)
            {
                factsWithKnownMetricsWithoutEligibleRule++;
            }
            else if (matchedAnyDeclaredMetric && !matchedAnyScopedMetric)
            {
                factsWithMetricsAtWrongScope++;
            }
        }

        var totalChecks = checksPassed + checksViolated;

        var rulesNotEvaluated = _rules
            .Where(rule =>
                !evaluatedRuleIds.Contains(rule.Id)
                && !notApplicableReasons.ContainsKey(rule.Id))
            .Select(rule => rule.Id)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var notEvaluatedReasons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (ruleId, reason) in unsupportedRuleReasons)
        {
            notEvaluatedReasons[ruleId] =
                $"Rule '{ruleId}' is not evaluable because its producer contract is unsupported: {reason}";
        }

        var eligibleFacts = factList
            .Where(fact => fact is not null)
            .ToList();

        foreach (var rule in applicableRules.Where(rule =>
                     !evaluatedRuleIds.Contains(rule.Id)))
        {
            var expectsProjectFacts = rule.Scope == RuleEvaluationScope.Project;
            var scopedFacts = eligibleFacts
                .Where(fact => IsProjectSummary(fact) == expectsProjectFacts)
                .ToList();

            var hasMetricEvidence = scopedFacts.Any(fact =>
                ArchitectureRuleMetricContracts.TryGetFactApplicability(
                    rule,
                    fact,
                    out _)
                && ArchitectureRuleMetricContracts.TryResolveMetric(
                    rule,
                    fact.Metrics,
                    out _,
                    out _));

            var hasWrongScopeMetricEvidence = eligibleFacts.Any(fact =>
                IsProjectSummary(fact) != expectsProjectFacts
                && ArchitectureRuleMetricContracts.TryGetFactApplicability(
                    rule,
                    fact,
                    out _)
                && ArchitectureRuleMetricContracts.TryResolveMetric(
                    rule,
                    fact.Metrics,
                    out _,
                    out _));

            var hasInapplicableMetricEvidence = eligibleFacts.Any(fact =>
                !ArchitectureRuleMetricContracts.TryGetFactApplicability(
                    rule,
                    fact,
                    out _)
                && ArchitectureRuleMetricContracts.TryResolveMetric(
                    rule,
                    fact.Metrics,
                    out _,
                    out _));

            var hasIncompleteScopedFacts = scopedFacts.Any(fact =>
            {
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

            var disabledProducerFact = eligibleFacts.FirstOrDefault(fact =>
                ArchitectureRuleMetricContracts.IsDeclaredProducer(
                    rule.Id,
                    fact.Source)
                && string.Equals(
                    fact.Metadata?.GetValueOrDefault("EvaluatorExecutionStatus"),
                    "DisabledByPolicy",
                    StringComparison.OrdinalIgnoreCase));

            var scopeLabel = rule.Scope.ToString().ToLowerInvariant();
            var producerDisposition =
                ArchitectureRuleMetricContracts.GetProducerContractDisposition(rule.Id);

            // Prefer explicit contract gaps and execution-state evidence over
            // generic missing-metric messages.
            notEvaluatedReasons[rule.Id] = producerDisposition is not null
                ? producerDisposition
                : disabledProducerFact is not null
                    ? $"Required evaluator '{disabledProducerFact.Source}' was disabled by policy: " +
                      (disabledProducerFact.Metadata?.GetValueOrDefault("StatusReason")
                       ?? "No status reason was provided.")
                : hasMetricEvidence
                ? $"Metric '{rule.MetricKey}' was emitted at {scopeLabel} scope, but no valid comparison completed. Check for non-finite observations or an invalid threshold."
                : hasWrongScopeMetricEvidence
                    ? $"Metric '{rule.MetricKey}' was emitted, but only at the wrong scope for rule '{rule.Id}' (required: {scopeLabel})."
                    : hasInapplicableMetricEvidence
                        ? $"Metric '{rule.MetricKey}' exists, but its producer contract or fact-level framework applicability does not match rule '{rule.Id}'; the metric was not reused."
                        : hasIncompleteScopedFacts
                            ? $"A {scopeLabel}-scope evaluator fact reports incomplete analysis; metric '{rule.MetricKey}' was withheld rather than treating partial evidence as a pass or zero."
                            : scopedFacts.Count == 0
                                ? $"No {scopeLabel}-scope evaluator facts were available for metric '{rule.MetricKey}'."
                                : $"No {scopeLabel}-scope evaluator fact emitted declared metric '{rule.MetricKey}'.";
        }

        var distinctPassedRules = evaluations
            .Where(evaluation => evaluation.Status == RuleEvaluationStatus.Passed)
            .Select(evaluation => evaluation.RuleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var distinctViolatedRules = evaluations
            .Where(evaluation => evaluation.Status == RuleEvaluationStatus.Violated)
            .Select(evaluation => evaluation.RuleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var stats = new RuleEvaluationStats
        {
            RulesRegistered = _rules.Count,
            RulesApplicable = applicableRules.Count,
            RulesUnsupported = unsupportedRuleReasons.Count,
            UnsupportedRuleReasons = unsupportedRuleReasons,
            RulesEvaluated = evaluatedRuleIds.Count,
            RulesPassed = distinctPassedRules,
            RulesViolated = distinctViolatedRules,
            EvaluationsPassed = checksPassed,
            EvaluationsViolated = checksViolated,
            EvaluationsNotEvaluated = notEvaluatedReasons.Count,
            ChecksPerformed = totalChecks,
            ChecksPassed = checksPassed,
            ChecksViolated = checksViolated,

            RulesNotEvaluated = rulesNotEvaluated,
            FactsWithoutMatchingScopedMetrics = factsWithoutMatchingScopedMetrics,
            FactsWithMetricsAtWrongScope = factsWithMetricsAtWrongScope,
            FactsWithKnownMetricsWithoutEligibleRule = factsWithKnownMetricsWithoutEligibleRule,

            RulesNotApplicable = notApplicableReasons.Keys
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList(),

            NotApplicableReasons = notApplicableReasons,
            NotEvaluatedReasons = notEvaluatedReasons,

            CheckBuckets = buckets
                .Select(kv => new RuleCheckBucket(
                    kv.Key.Domain,
                    kv.Key.Severity,
                    kv.Key.Category,
                    kv.Value))
                .ToList(),

            RulesEvaluatedByDomain = rulesByDomain.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Count,
                StringComparer.OrdinalIgnoreCase)
        };

        // This is a metric-production gap, not proof that a contract is
        // missing. TryResolveMetric only inspects metrics actually emitted
        // by the evaluator facts supplied to this invocation.
        if (rulesNotEvaluated.Count > 0)
        {
            var rulesById = _rules.ToDictionary(
                rule => rule.Id,
                StringComparer.OrdinalIgnoreCase);

            foreach (var ruleId in rulesNotEvaluated)
            {
                if (!rulesById.TryGetValue(ruleId, out var rule))
                {
                    continue;
                }

                _logger.LogWarning(
                    "Rule {RuleId} ({RuleName}) remains unevaluated. " +
                    "Metric={MetricKey}; Reason={Reason}",
                    rule.Id,
                    rule.Name,
                    rule.MetricKey,
                    notEvaluatedReasons.GetValueOrDefault(
                        rule.Id,
                        "No completed evaluation was recorded."));
            }
        }

        _logger.LogInformation(
            "RuleEngineCore processed {Facts} facts: {Violations} violations; " +
            "{Checks} checks ({Passed} passed, {Violated} violated); " +
            "{Evaluated}/{Registered} registered rules evaluated; " +
            "{SupportedEvaluated}/{SupportedApplicable} supported applicable rules evaluated " +
            "({Coverage:P1} coverage); " +
            "{NotApplicable} not applicable; {NotEvaluated} not evaluated; " +
            "{ProjectSummaries} project summaries processed; " +
            "{NonFiniteSkipped} non-finite metrics skipped; " +
            "{InvalidThresholds} invalid thresholds skipped; " +
            "{FactsWithoutMetrics} facts emitted only undeclared metric keys; " +
            "{KnownButIneligible} facts emitted registered metrics with no eligible rule; " +
            "{WrongScopeFacts} facts had applicable metrics only at the wrong scope.",
            factList.Count,
            results.Count,
            totalChecks,
            checksPassed,
            checksViolated,
            stats.RulesEvaluated,
            stats.RulesRegistered,
            stats.RulesEvaluated,
            stats.RulesApplicable,
            stats.EvaluationCoverage,
            stats.RulesNotApplicable.Count,
            stats.RulesNotEvaluated.Count,
            projectSummaryFacts,
            skippedNonFiniteMetrics,
            skippedInvalidThresholds,
            factsWithoutMatchingScopedMetrics,
            factsWithKnownMetricsWithoutEligibleRule,
            factsWithMetricsAtWrongScope);


        return new RuleEvaluationOutcome(results, stats, evaluations); 
    }

    private static bool IsProjectSummary(ArchitectureEvaluatorResult fact)
    {
        var scope = fact.Metadata?.GetValueOrDefault(EvaluationScopeKey);

        if (string.Equals(
                scope,
                ProjectScope,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Explicit metadata takes precedence over legacy category inference.
        if (string.Equals(
                scope,
                "File",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(
            fact.Category,
            ArchitectureSummaryCategory,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveLayerCached(
        string? filePath,
        ProjectArchitectureContext context,
        Dictionary<string, string> cache)
    {
        var key = filePath ?? string.Empty;

        if (!cache.TryGetValue(key, out var layer))
        {
            layer = ResolveLayer(filePath, context);
            cache[key] = layer;
        }

        return layer;
    }

    private static ArchitectureRuleresult CreateRuleResult(
        ArchitectureRuleDefinition rule,
        ArchitectureEvaluatorResult fact,
        ProjectArchitectureContext context,
        string layer,
        string metric,
        double value,
        double threshold,
        DateTimeOffset evaluatedAt)
    {
        var category = ParseCategory(rule.Category);

        var filePath = rule.Scope == RuleEvaluationScope.Project
            ? string.Empty
            : fact.Metadata?.GetValueOrDefault(FilePathKey)
              ?? fact.Target;

        var @namespace =
            fact.Metadata?.GetValueOrDefault("Namespace");

        return new ArchitectureRuleresult(
            rule.Id,
            rule.Name,
            category,
            rule.Severity,
            filePath,
            @namespace,
            $"[{fact.Source}] Metric '{metric}' = {value:0.##}, " +
            $"threshold = {threshold:0.##}. {rule.Recommendation}",
            evaluatedAt,
            isCompliant: false)
        {
            DetectedBy = fact.Source,
            Domain = layer,
            Target = rule.Scope == RuleEvaluationScope.Project
                ? fact.ProjectName ?? fact.Target
                : fact.Target,
            AnalyzerVersion = context.DetectorVersion
        };
    }

    private static string ResolveLayer(
        string? filePath,
        ProjectArchitectureContext context)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "Unclassified";
        }

        var normalizedFilePath = NormalizePath(filePath);

        var module = context.Modules
            .Where(m => !string.IsNullOrWhiteSpace(m.Path))
            .Select(m => new
            {
                Module = m,
                NormalizedPath = NormalizePath(m.Path)
            })
            .Where(m => IsSameOrDescendantPath(
                normalizedFilePath,
                m.NormalizedPath))
            .OrderByDescending(m => m.NormalizedPath.Length)
            .Select(m => m.Module)
            .FirstOrDefault();

        module ??= context.Modules
            .Where(m => !string.IsNullOrWhiteSpace(m.Path))
            .OrderByDescending(m => m.Path.Length)
            .FirstOrDefault(m =>
                ContainsPathSegment(
                    normalizedFilePath,
                    Path.GetFileName(
                        m.Path.TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar))));

        var layerName = module?.Layers.FirstOrDefault()?.Name;

        return string.IsNullOrWhiteSpace(layerName)
            ? "Unclassified"
            : layerName;
    }

    private static bool IsSameOrDescendantPath(
        string candidate,
        string parent)
    {
        if (string.Equals(
                candidate,
                parent,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = parent.EndsWith(Path.DirectorySeparatorChar)
            ? parent
            : parent + Path.DirectorySeparatorChar;

        return candidate.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
        }
    }

    private static bool ContainsPathSegment(string path, string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var parts = path.Split(
            [
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            ],
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(part => string.Equals(
            part,
            segment,
            StringComparison.OrdinalIgnoreCase));
    }

    private static ArchitectureRuleCategory ParseCategory(string category) =>
        Enum.TryParse(
            category,
            ignoreCase: true,
            out ArchitectureRuleCategory parsed)
            ? parsed
            : ArchitectureRuleCategory.General;

    private double ResolveThreshold(ArchitectureRuleDefinition rule)
    {
        var overrides = _policy.RuleThresholds;

        if (overrides is not null)
        {
            // A rule-specific override takes precedence over a metric override.
            foreach (var entry in overrides)
            {
                if (string.Equals(
                        entry.Key,
                        rule.Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }

            foreach (var entry in overrides)
            {
                if (string.Equals(
                        entry.Key,
                        rule.MetricKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }
        }

        return rule.Threshold;
    }

    private static bool IsViolation(
        ComparisonOperator operation,
        double value,
        double threshold)
    {
        if (!double.IsFinite(value) || !double.IsFinite(threshold))
        {
            return false;
        }

        return operation switch
        {
            ComparisonOperator.LessThan => value < threshold,
            ComparisonOperator.GreaterThan => value > threshold,
            ComparisonOperator.LessOrEqual => value <= threshold,
            ComparisonOperator.GreaterOrEqual => value >= threshold,
            ComparisonOperator.Equal => Math.Abs(value - threshold) < 0.0001,
            ComparisonOperator.NotEqual => Math.Abs(value - threshold) >= 0.0001,
            _ => false
        };
    }
}
