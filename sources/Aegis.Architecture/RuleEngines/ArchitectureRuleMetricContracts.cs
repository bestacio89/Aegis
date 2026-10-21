
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
