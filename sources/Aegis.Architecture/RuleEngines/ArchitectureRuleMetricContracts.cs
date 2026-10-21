
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
                ["AEG-ARCH-R4"] = ["ArchitecturalEvaluator"],
                ["AEG-ARCH-R5"] = ["ArchitecturalEvaluator"],
                ["AEG-ARCH-R6"] = ["ArchitecturalEvaluator"],
                ["AEG-ARCH-R7"] = ["ApiConsistencyEvaluator"],
                ["AEG-ARCH-R8"] = ["GodClassEvaluator"],
                ["AEG-ARCH-R9"] = ["ArchitecturalEvaluator"],
                ["AEG-ARCH-R10"] = ["SecurityEvaluator"],
                ["AEG-ARCH-R11"] = ["DocumentationEvaluator"],
                ["AEG-ARCH-R12"] = ["NodeEvaluator"],

                ["AEG-BACK-R1"] = ["CohesionEvaluator"],
                ["AEG-BACK-R2"] = ["ComplexityEvaluator"],
                ["AEG-BACK-R3"] = ["DependencyGraphEvaluator"],
                ["AEG-BACK-R4"] = ["ErrorHandlingEvaluator"],
                ["AEG-BACK-R5"] = ["MaintainabilityEvaluator"],

                ["AEG-DEPS-R1"] = ["PackageVersionEvaluator"],
                ["AEG-DEPS-R2"] = ["PackageVersionEvaluator"],
                ["AEG-DEPS-R3"] = ["UnusedReferenceEvaluator"],
                ["AEG-DEPS-R4"] = ["PackageVersionEvaluator"],

                ["AEG-DES-ADM001"] = ["AnemicDomainModelEvaluator"],
                ["AEG-DES-BLD001"] = ["BuilderPatternEvaluator"],
                ["AEG-DES-BLD002"] = ["BuilderPatternEvaluator"],
                ["AEG-DES-CMD001"] = ["CommandPatternEvaluator"],
                ["AEG-DES-CMD002"] = ["CommandPatternEvaluator"],
                ["AEG-DES-DEC001"] = ["DecoratorPatternEvaluator"],
                ["AEG-DES-DEC002"] = ["DecoratorPatternEvaluator"],
                ["AEG-DES-FAC001"] = ["FacadePatternEvaluator"],
                ["AEG-DES-FAC002"] = ["FacadePatternEvaluator"],
                ["AEG-DES-FACR001"] = ["FactoryPatternEvaluator"],
                ["AEG-DES-FACR002"] = ["FactoryPatternEvaluator"],
                ["AEG-DES-GOD001"] = ["GodClassEvaluator"],
                ["AEG-DES-MEDI001"] = ["MediatorPatternEvaluator"],
                ["AEG-DES-MEDI002"] = ["MediatorPatternEvaluator"],
                ["AEG-DES-OBS001"] = ["ObserverPatternEvaluator"],
                ["AEG-DES-OBS002"] = ["ObserverPatternEvaluator"],
                ["AEG-DES-REPO001"] = ["RepositoryPatternEvaluator"],
                ["AEG-DES-REPO002"] = ["RepositoryPatternEvaluator"],
                ["AEG-DES-SING001"] = ["SingletonPatternEvaluator"],
                ["AEG-DES-SING002"] = ["SingletonPatternEvaluator"],
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
                ["AEG-FRONT-R11"] = ["RazorEvaluator"],

                ["AEG-INF-R1"] = ["ConfigurationEvaluator"],
                ["AEG-INF-R2"] = ["LoggingEvaluator"],
                ["AEG-INF-R3"] = ["RepositoryEvaluator"],

                ["AEG-NAM-R1"] = ["NamingEvaluator"],
                ["AEG-NAM-R2"] = ["NamingEvaluator"],
                ["AEG-NAM-R3"] = ["NamingEvaluator"],
                ["AEG-NAM-R4"] = ["NamingEvaluator"],
                ["AEG-NAM-R5"] = ["NamingEvaluator"],
                ["AEG-NAM-R6"] = ["NamingEvaluator"],

                ["AEG-PERF-R1"] = ["PerformanceEvaluator"],
                ["AEG-PERF-R2"] = ["PerformanceEvaluator"],
                ["AEG-PERF-R3"] = ["PerformanceEvaluator"],
                ["AEG-PERF-R4"] = ["PerformanceEvaluator"],

                ["AEG-TXN-R1"] = ["ArchitecturalTransactionEvaluator"],
                ["AEG-TXN-R2"] = ["ArchitecturalTransactionEvaluator"],
                ["AEG-TXN-R3"] = ["ArchitecturalTransactionEvaluator"],
                ["AEG-TXN-R4"] = ["ArchitecturalTransactionEvaluator"]
            };

    private static readonly IReadOnlyDictionary<string, string>
        UnsupportedRuleProducerContracts =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AEG-DES-CIRC001"] =
                    "No dedicated pattern-circular-reference producer exists yet; CircularDependencyEvaluator measures module dependency cycles and is not a semantic substitute.",
                ["AEG-DES-OVER001"] =
                    "No dedicated pattern-overuse producer exists yet; repeated pattern detections must not be treated as overuse without an explicit policy and denominator."
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

        // A root context with multiple discovered modules is not a complete
        // framework inventory: language-specific project detectors run inside
        // each module. Keep rules eligible at repository level and require the
        // matching framework on each individual fact below.
        if (context.IsMultiModule)
            return true;

        // No recognized family means applicability remains unknown.
        if (detectedFamilies.Length == 0)
            return true;

        // R11 has no separate metric key, so it is only a fallback for Razor
        // files, whose ruleset has no dedicated complexity rule. In a mixed
        // repository it remains enabled when Razor is one of the detected
        // families; per-file applicability below prevents cross-framework reuse.
        if (targetScope == FrontendScope.CrossFramework)
        {
            if (detectedFamilies.Contains(FrontendScope.Razor))
                return true;

            var onlyDedicatedFamilies = detectedFamilies.All(family =>
                family is FrontendScope.Angular
                    or FrontendScope.React
                    or FrontendScope.Blazor);

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
            FrontendScope.CrossFramework => family.Value == FrontendScope.Razor,
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
