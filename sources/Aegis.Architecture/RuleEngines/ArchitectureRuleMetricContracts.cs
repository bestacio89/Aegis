
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Architecture.RuleEngines;

/// <summary>
/// Explicit compatibility boundary between rule definitions and evaluator metrics.
///
/// A metric is resolved only when its key matches the rule's declared MetricKey.
/// Legacy aliases must not be added until their meaning, units, and scope have
/// been verified against the evaluator that produces them.
/// </summary>
internal static class ArchitectureRuleMetricContracts
{
    private enum FrontendScope
    {
        Angular,
        React,
        Razor,
        Blazor,
        CrossFramework
    }

    // A metric name alone is not sufficient proof that a rule's producer
    // measured the intended concept. These contracts bind rules to the evaluator
    // families that own their evidence. Multiple producers are allowed only
    // where they implement the same declared rule semantics.
    private static readonly IReadOnlyDictionary<string, string[]>
        RuleProducerContracts =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["AEG-ARCH-R1"] = ["CircularDependencyEvaluator"],
                ["AEG-ARCH-R2"] = ["ArchitecturalEvaluator"],
                ["AEG-ARCH-R3"] = ["ArchitecturalEvaluator"],
                ["AEG-ARCH-R5"] = ["ArchitecturalEvaluator"],
                ["AEG-ARCH-R7"] = ["ApiConsistencyEvaluator"],
                ["AEG-ARCH-R8"] = ["GodClassEvaluator"],
                ["AEG-ARCH-R11"] = ["DocumentationEvaluator"],
                ["AEG-BACK-R2"] = ["ComplexityEvaluator"],
                ["AEG-BACK-R3"] = ["DependencyGraphEvaluator"],
                ["AEG-BACK-R4"] = ["ErrorHandlingEvaluator"],
                ["AEG-BACK-R5"] = ["MaintainabilityEvaluator"],

                ["AEG-DES-ADM001"] = ["AnemicDomainModelEvaluator"],
                ["AEG-DES-BLD001"] = ["BuilderPatternEvaluator"],
                ["AEG-DES-BLD002"] = ["BuilderPatternEvaluator"],
                ["AEG-DES-CMD001"] = ["CommandPatternEvaluator"],
                ["AEG-DES-DEC001"] = ["DecoratorPatternEvaluator"],
                ["AEG-DES-FAC001"] = ["FacadePatternEvaluator"],
                ["AEG-DES-FAC002"] = ["FacadePatternEvaluator"],
                ["AEG-DES-FACR001"] = ["FactoryPatternEvaluator"],
                ["AEG-DES-GOD001"] = ["GodClassEvaluator"],
                ["AEG-DES-MEDI001"] = ["MediatorPatternEvaluator"],
                ["AEG-DES-OBS001"] = ["ObserverPatternEvaluator"],
                ["AEG-DES-REPO001"] = ["RepositoryPatternEvaluator"],
                ["AEG-DES-SING001"] = ["SingletonPatternEvaluator"],
                ["AEG-DES-STRAT001"] = ["StrategyPatternEvaluator"],
                ["AEG-DES-STRAT002"] = ["StrategyPatternEvaluator"],

                ["AEG-FRONT-R1"] = ["AngularEvaluator"],
                ["AEG-FRONT-R2"] = ["AngularEvaluator"],
                ["AEG-FRONT-R3"] = ["ReactEvaluator"],
                ["AEG-FRONT-R4"] = ["ReactEvaluator"],
                ["AEG-FRONT-R5"] = ["RazorEvaluator"],
                ["AEG-FRONT-R6"] = ["RazorEvaluator"],
                ["AEG-FRONT-R7"] = ["RazorEvaluator"],
                ["AEG-FRONT-R8"] = ["BlazorEvaluator"],
                ["AEG-FRONT-R9"] = ["BlazorEvaluator"],
                ["AEG-FRONT-R10"] = ["BlazorEvaluator"],
                ["AEG-FRONT-R11"] = ["RazorEvaluator", "ReactEvaluator"],

                ["AEG-INF-R1"] = ["ConfigurationEvaluator"],
                ["AEG-INF-R2"] = ["LoggingEvaluator"],
                ["AEG-INF-R3"] = ["RepositoryEvaluator"],

                ["AEG-NAM-R1"] = ["NamingEvaluator"],
                ["AEG-NAM-R2"] = ["NamingEvaluator"],
                ["AEG-NAM-R3"] = ["NamingEvaluator"],
                ["AEG-NAM-R4"] = ["NamingEvaluator"],
                ["AEG-NAM-R5"] = ["NamingEvaluator"],
                ["AEG-NAM-R6"] = ["NamingEvaluator"],
                ["AEG-NAM-R7"] = ["NamingEvaluator"],
                ["AEG-NAM-R8"] = ["NamingEvaluator"],
                ["AEG-NAM-R9"] = ["NamingEvaluator"],

                ["AEG-PERF-R1"] = ["PerformanceEvaluator"],
                ["AEG-PERF-R2"] = ["PerformanceEvaluator"],
                ["AEG-PERF-R4"] = ["PerformanceEvaluator"],

                ["AEG-TXN-R1"] = ["ArchitecturalTransactionEvaluator"],
            };

    private static readonly IReadOnlyDictionary<string, string>
        UnsupportedRuleProducerContracts =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AEG-DES-CIRC001"] =
                    "No dedicated pattern-circular-reference producer exists yet; CircularDependencyEvaluator measures module dependency cycles and is not a semantic substitute.",
                ["AEG-DES-OVER001"] =
                    "No dedicated pattern-overuse producer exists yet; repeated pattern detections must not be treated as overuse without an explicit policy and denominator.",
                ["AEG-ARCH-R4"] = "The current architecture producer does not emit a validated cross-domain leakage metric; forbidden references and layer violations are not semantic substitutes.",
                ["AEG-ARCH-R6"] = "No file-scope controller business-logic density producer is registered yet.",
                ["AEG-ARCH-R9"] = "The current cohesion evaluator emits structural counts and ratios, not a validated layer-cohesion index.",
                ["AEG-ARCH-R10"] = "The security evaluator emits evidence counts but no calibrated SecurityComplianceIndex; a score must not be invented from heterogeneous findings.",
                ["AEG-ARCH-R12"] = "NodeEvaluator does not currently emit a file-scope NodeCouplingIndex.",
                ["AEG-BACK-R1"] = "CohesionEvaluator emits member and method/field ratios, not a validated cohesion index.",
                ["AEG-DEPS-R1"] = "PackageVersionEvaluator does not perform registry-backed outdated-version comparisons, so OutdatedPackageRatio is unavailable.",
                ["AEG-DEPS-R2"] = "PackageVersionEvaluator does not produce a validated dependency-freshness metric.",
                ["AEG-DEPS-R3"] = "UnusedReferenceEvaluator does not establish actual code usage; a manifest parse is not proof that a reference is unused.",
                ["AEG-DEPS-R4"] = "PackageVersionEvaluator does not emit a comparable major-version drift metric.",
                ["AEG-DES-DEC002"] = "DecoratorPatternEvaluator does not currently measure decorator self-reference; structural delegation signals are not a substitute.",
                ["AEG-DES-REPO002"] = "RepositoryPatternEvaluator's persistence/transport indicators include legitimate repository behavior and do not prove prohibited external I/O.",
                ["AEG-DES-FACR002"] = "FactoryPatternEvaluator measures instantiation inside factory candidates, where construction is expected; it does not measure direct construction outside a factory boundary.",
                ["AEG-DES-CMD002"] = "CommandPatternEvaluator pairs command and handler names but does not measure handler overloads.",
                ["AEG-DES-MEDI002"] = "MediatorPatternEvaluator does not resolve direct handler calls independently from service-like coupling signals.",
                ["AEG-DES-OBS002"] = "ObserverPatternEvaluator does not emit a file-scope observer coupling index.",
                ["AEG-DES-SING002"] = "SingletonPatternEvaluator does not currently measure global-state usage.",
                ["AEG-PERF-R3"] = "No runtime allocation profiler is integrated; static lexical analysis cannot claim a memory allocation rate.",
                ["AEG-TXN-R2"] = "ArchitecturalTransactionEvaluator does not emit a dedicated data-access hygiene index.",
                ["AEG-TXN-R3"] = "ArchitecturalTransactionEvaluator does not measure connection-pool abuse.",
                ["AEG-TXN-R4"] = "ArchitecturalTransactionEvaluator does not prove uncommitted transactions from its current static evidence."
            };

    private static readonly IReadOnlyDictionary<string, FrontendScope>
        FrontendRuleScopes =
            new Dictionary<string, FrontendScope>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["AEG-FRONT-R1"] = FrontendScope.Angular,
                ["AEG-FRONT-R2"] = FrontendScope.Angular,

                ["AEG-FRONT-R3"] = FrontendScope.React,
                ["AEG-FRONT-R4"] = FrontendScope.React,

                ["AEG-FRONT-R5"] = FrontendScope.Razor,
                ["AEG-FRONT-R6"] = FrontendScope.Razor,
                ["AEG-FRONT-R7"] = FrontendScope.Razor,

                ["AEG-FRONT-R8"] = FrontendScope.Blazor,
                ["AEG-FRONT-R9"] = FrontendScope.Blazor,
                ["AEG-FRONT-R10"] = FrontendScope.Blazor,

                // Applies to supported frontend technologies generally.
                ["AEG-FRONT-R11"] = FrontendScope.CrossFramework
            };

    public static string? GetProducerContractDisposition(string ruleId)
    {
        if (RuleProducerContracts.ContainsKey(ruleId))
            return null;

        if (UnsupportedRuleProducerContracts.TryGetValue(ruleId, out var reason))
            return reason;

        return $"No producer-to-rule contract is registered for rule '{ruleId}'.";
    }

    /// <summary>
    /// Resolves a rule's declared metric key.
    ///
    /// Matching is case-insensitive, but no semantic aliases are inferred.
    /// If multiple metric keys differ only by casing, resolution fails as
    /// ambiguous rather than choosing an arbitrary measurement.
    /// </summary>
    public static bool TryResolveMetric(
        ArchitectureRuleDefinition rule,
        IReadOnlyDictionary<string, double> metrics,
        out string metricKey,
        out double value)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(metrics);

        metricKey = string.Empty;
        value = default;

        if (string.IsNullOrWhiteSpace(rule.MetricKey))
            return false;

        // Prefer the exact declared spelling when the dictionary supports it.
        if (metrics.TryGetValue(rule.MetricKey, out var exactValue))
        {
            metricKey = rule.MetricKey;
            value = exactValue;
            return true;
        }

        // Support case-insensitive key matching without allowing ambiguity.
        var matches = metrics
            .Where(pair => string.Equals(
                pair.Key,
                rule.MetricKey,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        if (matches.Length != 1)
            return false;

        metricKey = matches[0].Key;
        value = matches[0].Value;
        return true;
    }

    /// <summary>
    /// Determines whether a frontend rule is compatible with the detected
    /// frontend family.
    ///
    /// Unknown technology is not treated as proof that a rule is inapplicable.
    /// A positively identified, different frontend family excludes a
    /// framework-specific rule.
    /// </summary>
    public static bool TryGetApplicability(
        ArchitectureRuleDefinition rule,
        ProjectArchitectureContext context,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(context);

        reason = string.Empty;

        if (IsCSharpOnlyNamingRule(rule.Id)
            && !string.Equals(context.Language, "C#", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Rule '{rule.Id}' applies only to C# field/property declarations.";
            return false;
        }

        if (!FrontendRuleScopes.TryGetValue(rule.Id, out var targetScope))
            return true;

        var detectedFrameworks = new List<string>();

        if (!string.IsNullOrWhiteSpace(context.Framework))
            detectedFrameworks.Add(context.Framework);

        if (context.DetectedFrameworks is not null)
        {
            detectedFrameworks.AddRange(
                context.DetectedFrameworks.Where(
                    framework => !string.IsNullOrWhiteSpace(framework)));
        }

        var detectedFamilies = detectedFrameworks
            .Select(ResolveFrontendFamily)
            .Where(family => family.HasValue)
            .Select(family => family!.Value)
            .Distinct()
            .ToArray();

        // The repository context aggregates framework evidence discovered
        // from project scopes. In a multi-module repository, retain each
        // detected family independently; do not make every frontend ruleset
        // eligible merely because the repository contains multiple modules.
        // Individual facts are still checked against their own framework below.

        // No recognized family means applicability remains unknown.
        if (detectedFamilies.Length == 0)
            return true;

        // R11 is the shared complexity rule for React and Razor files, which
        // have no dedicated complexity rule in this ruleset. Angular and Blazor
        // use their dedicated rules; per-file applicability prevents metric reuse.
        if (targetScope == FrontendScope.CrossFramework)
        {
            if (detectedFamilies.Any(family =>
                    family is FrontendScope.Razor or FrontendScope.React))
            {
                return true;
            }

            var onlyDedicatedFamilies = detectedFamilies.All(family =>
                family is FrontendScope.Angular or FrontendScope.Blazor);

            if (onlyDedicatedFamilies)
            {
                reason =
                    $"Rule '{rule.Id}' is not applicable because the detected " +
                    "frontend families have dedicated complexity rules; the " +
                    "shared ComplexityScore metric would duplicate their checks.";

                return false;
            }

            return true;
        }

        var targetFamily = targetScope switch
        {
            FrontendScope.Angular => FrontendScope.Angular,
            FrontendScope.React => FrontendScope.React,
            FrontendScope.Razor => FrontendScope.Razor,
            FrontendScope.Blazor => FrontendScope.Blazor,
            _ => throw new InvalidOperationException(
                $"Unsupported frontend scope '{targetScope}'.")
        };

        if (detectedFamilies.Contains(targetFamily))
            return true;

        reason =
            $"Rule '{rule.Id}' targets frontend family '{targetFamily}', " +
            $"but the detected family/families are " +
            $"[{string.Join(", ", detectedFamilies)}].";

        return false;
    }

    /// <summary>
    /// Enforces frontend family applicability at the fact boundary. Project
    /// context can describe a mixed repository, so it must not authorize a
    /// React metric to satisfy a Blazor rule (or vice versa).
    /// </summary>
    public static bool TryGetFactApplicability(
        ArchitectureRuleDefinition rule,
        ArchitectureEvaluatorResult fact,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(fact);

        reason = string.Empty;

        if (UnsupportedRuleProducerContracts.TryGetValue(rule.Id, out var unsupportedReason))
        {
            reason = $"Rule '{rule.Id}' is explicitly unsupported: {unsupportedReason}";
            return false;
        }

        if (!RuleProducerContracts.TryGetValue(rule.Id, out var allowedProducers))
        {
            reason = $"Rule '{rule.Id}' has no producer contract; metric evidence is not eligible.";
            return false;
        }

        if (!allowedProducers.Contains(fact.Source, StringComparer.OrdinalIgnoreCase))
        {
            reason =
                $"Rule '{rule.Id}' requires evidence from [{string.Join(", ", allowedProducers)}], " +
                $"but fact producer '{fact.Source}' is not a declared producer for this rule.";
            return false;
        }

        if (IsCSharpOnlyNamingRule(rule.Id))
        {
            var language = fact.Metadata?.GetValueOrDefault("Language");
            if (!string.Equals(language, "C#", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Rule '{rule.Id}' applies only to C# field/property declarations.";
                return false;
            }

            return true;
        }

        if (!FrontendRuleScopes.TryGetValue(rule.Id, out var targetScope))
            return true;

        var framework = fact.Metadata?.GetValueOrDefault("Framework");

        if (string.IsNullOrWhiteSpace(framework))
        {
            reason =
                $"Rule '{rule.Id}' requires file-level frontend framework " +
                "evidence, but the evaluator fact does not declare Framework metadata.";

            return false;
        }

        var family = ResolveFrontendFamily(framework);

        if (!family.HasValue)
        {
            reason =
                $"Rule '{rule.Id}' cannot establish frontend applicability " +
                $"from unrecognized framework metadata '{framework}'.";

            return false;
        }

        var applies = targetScope switch
        {
            FrontendScope.CrossFramework => family.Value is FrontendScope.Razor or FrontendScope.React,
            _ => family.Value == targetScope
        };

        if (applies)
            return true;

        reason = targetScope == FrontendScope.CrossFramework
            ? $"Rule '{rule.Id}' is a Razor fallback and does not apply to " +
              $"file framework '{framework}'."
            : $"Rule '{rule.Id}' targets frontend family '{targetScope}', " +
              $"but this fact belongs to '{family.Value}'.";

        return false;
    }

    private static bool IsCSharpOnlyNamingRule(string ruleId) =>
        ruleId.Equals("AEG-NAM-R8", StringComparison.OrdinalIgnoreCase)
        || ruleId.Equals("AEG-NAM-R9", StringComparison.OrdinalIgnoreCase);

    private static FrontendScope? ResolveFrontendFamily(string framework)
    {
        if (framework.Contains(
                "Angular",
                StringComparison.OrdinalIgnoreCase))
        {
            return FrontendScope.Angular;
        }

        if (framework.Contains(
                "React",
                StringComparison.OrdinalIgnoreCase))
        {
            return FrontendScope.React;
        }

        if (framework.Contains(
                "Blazor",
                StringComparison.OrdinalIgnoreCase))
        {
            return FrontendScope.Blazor;
        }

        if (framework.Contains(
                "Razor",
                StringComparison.OrdinalIgnoreCase) ||
            framework.Contains(
                "MVC",
                StringComparison.OrdinalIgnoreCase) ||
            framework.Contains(
                "Razor Pages",
                StringComparison.OrdinalIgnoreCase))
        {
            return FrontendScope.Razor;
        }

        return null;
    }
}
