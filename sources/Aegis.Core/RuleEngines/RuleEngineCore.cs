using Microsoft.Extensions.Logging;

using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Architecture.RuleEngines;

/// <summary>
/// 🧠 Converts evaluator facts into deterministic architecture rule results.
///
/// Evaluators only produce facts.
/// This component interprets those facts using registered rules and policies.
/// </summary>
public sealed class RuleEngineCore
{
    private readonly ILogger<RuleEngineCore> _logger;

    private readonly IReadOnlyList<ArchitectureRuleDefinition> _rules;

    private AegisArchitecturePolicy _policy;



    public RuleEngineCore(
        ILogger<RuleEngineCore> logger,
        AegisArchitecturePolicy policy)
    {
        _logger = logger;

        _policy = policy;

        _rules =
            ArchitectureRuleRegistry.All;
    }



    // ======================================================
    // 🧩 Policy synchronization
    // ======================================================

    /// <summary>
    /// Updates runtime rule interpretation policy.
    /// Rule definitions themselves remain immutable.
    /// </summary>
    public void ApplyPolicy(
        AegisArchitecturePolicy newPolicy)
    {
        _policy = newPolicy;


        _logger.LogInformation(
            "Aegis rule policy applied → {Name} (v{Version})",
            newPolicy.Name ?? "Unnamed",
            newPolicy.Version ?? "1.0");
    }



    // ======================================================
    // 🧮 Evaluation
    // ======================================================

    public IEnumerable<ArchitectureRuleresult> Evaluate(
        ProjectArchitectureContext context,
        IEnumerable<ArchitectureEvaluatorResult> facts)
    {
        var factList =
            facts.ToList();


        var results =
            new List<ArchitectureRuleresult>();


        int totalChecks = 0;



        foreach (var fact in factList)
        {
            foreach (var metric in fact.Metrics)
            {
                var applicableRules =
                    _rules.Where(rule =>
                        rule.MetricKey.Equals(
                            metric.Key,
                            StringComparison.OrdinalIgnoreCase));



                foreach (var rule in applicableRules)
                {
                    totalChecks++;


                    var threshold =
                        ResolveThreshold(rule);



                    if (!IsViolation(
                        rule.Operator,
                        metric.Value,
                        threshold))
                    {
                        continue;
                    }



                    results.Add(
                        CreateRuleResult(
                            rule,
                            fact,
                            context,
                            metric.Key,
                            metric.Value,
                            threshold));
                }
            }
        }



        _logger.LogInformation(
            "RuleEngineCore evaluated {Facts} facts → {Results} violations ({Checks} checks).",
            factList.Count,
            results.Count,
            totalChecks);



        return results;
    }



    // ======================================================
    // 🏗️ Rule Result Creation
    // ======================================================

    private static ArchitectureRuleresult CreateRuleResult(
        ArchitectureRuleDefinition rule,
        ArchitectureEvaluatorResult fact,
        ProjectArchitectureContext context,
        string metric,
        double value,
        double threshold)
    {
        var category =
            ParseCategory(
                rule.Category);


        // The evaluator's real file path lives in Metadata (e.g. NamingEvaluator sets
        // Metadata["FilePath"] = file); fact.Target is a coarser identifier (often just a
        // bare filename) and belongs in the Target field, not FilePath. Previously both
        // FilePath and Target were wrong: FilePath held fact.Target, and the dedicated
        // Target field (meant for class/metric-level detail per its own doc comment) was
        // never set at all.
        var filePath =
            fact.Metadata?.GetValueOrDefault("FilePath")
            ?? fact.Target;


        // fact.Source is the evaluator's display name ("Naming Convention Evaluator"),
        // not a namespace — using it here was mislabeling every row. The real namespace,
        // when an evaluator captures one, lives in Metadata.
        var @namespace =
            fact.Metadata?.GetValueOrDefault("Namespace");


        var layer =
            ResolveLayer(
                filePath,
                context);


        return new ArchitectureRuleresult(
            rule.Id,
            rule.Name,
            category,
            rule.Severity,
            filePath,
            @namespace,
            $"[{fact.Source}] Metric '{metric}' = {value:0.##}, threshold = {threshold:0.##}. {rule.Recommendation}",
            DateTimeOffset.UtcNow,
            isCompliant: false)
        {
            DetectedBy =
                fact.Source,


            Domain =
                layer,


            Target =
                fact.Target,


            AnalyzerVersion =
                context.DetectorVersion
        };
    }



    /// <summary>
    /// Resolves which real, detected architecture layer (from context.Modules/Layers —
    /// e.g. Api, Application, Domain, Infrastructure) a violated file belongs to, by matching
    /// the file's path against each module's root path.
    ///
    /// Hardened against path-format mismatches: the original strict StartsWith comparison
    /// silently failed whenever module.Path and filePath differed in ways that don't change
    /// what location they actually point to — one relative and one absolute, different slash
    /// direction, or a trailing separator. Both sides are now normalized through
    /// Path.GetFullPath before comparing, and a more forgiving segment-based fallback catches
    /// cases that still don't align after normalization.
    ///
    /// Falls back to "Unclassified" — not "General" — when no module claims the file, since
    /// "Unclassified" honestly signals "we couldn't attribute this" rather than reading as an
    /// intentional category.
    /// </summary>
    private static string ResolveLayer(
        string? filePath,
        ProjectArchitectureContext context)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "Unclassified";
        }


        var normalizedFilePath =
            NormalizePath(filePath);


        var module =
            context.Modules
                .Where(m => !string.IsNullOrWhiteSpace(m.Path))
                .Select(m => new
                {
                    Module = m,
                    NormalizedPath = NormalizePath(m.Path)
                })
                .Where(m =>
                    normalizedFilePath.StartsWith(
                        m.NormalizedPath,
                        StringComparison.OrdinalIgnoreCase))
                // Most specific (longest) path wins, in case modules are nested.
                .OrderByDescending(m => m.NormalizedPath.Length)
                .Select(m => m.Module)
                .FirstOrDefault();


        // Strict prefix match still failed — most likely the two paths were captured
        // relative to genuinely different roots (e.g. a CI checkout path vs. a local dev
        // path) rather than just differing in format. Fall back to checking whether the
        // module's own folder name appears as a distinct, separator-bounded segment
        // anywhere in the file path — still avoids false positives from substring
        // collisions like "Aegis.Core" matching inside "Aegis.Core.Tests".
        module ??=
            context.Modules
                .Where(m => !string.IsNullOrWhiteSpace(m.Path))
                .OrderByDescending(m => m.Path.Length)
                .FirstOrDefault(m =>
                    ContainsPathSegment(
                        normalizedFilePath,
                        Path.GetFileName(
                            m.Path.TrimEnd(
                                Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar))));


        var layerName =
            module?.Layers.FirstOrDefault()?.Name;


        return string.IsNullOrWhiteSpace(layerName)
            ? "Unclassified"
            : layerName;
    }



    /// <summary>
    /// Normalizes a path for comparison: resolves it to a full, absolute path and trims a
    /// trailing separator, so a relative path and an absolute path pointing at the same
    /// location, or paths using different separator characters, compare equal.
    ///
    /// Caveat: Path.GetFullPath resolves relative paths against the current working
    /// directory of the running process, not necessarily the project root. If module.Path
    /// or filePath are ever stored as relative paths, verify the process's working
    /// directory matches what they're relative to — otherwise this can normalize a
    /// relative path to the wrong absolute location. If both inputs are already absolute
    /// (the common case for paths captured from Directory.EnumerateFiles on an absolute
    /// root), this is effectively just separator/casing normalization and this caveat
    /// doesn't apply.
    /// </summary>
    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
        }
        catch
        {
            // Path.GetFullPath throws on some malformed inputs (invalid characters, etc.) —
            // fall back to a best-effort normalization rather than letting a single bad
            // path value crash the whole scan.
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


        var parts =
            path.Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                },
                StringSplitOptions.RemoveEmptyEntries);


        return parts.Any(p =>
            string.Equals(
                p,
                segment,
                StringComparison.OrdinalIgnoreCase));
    }



    private static ArchitectureRuleCategory ParseCategory(
        string category)
    {
        return Enum.TryParse(
            category,
            true,
            out ArchitectureRuleCategory parsed)
                ? parsed
                : ArchitectureRuleCategory.General;
    }



    // ======================================================
    // 🎚️ Threshold Resolution
    // ======================================================

    private double ResolveThreshold(
        ArchitectureRuleDefinition rule)
    {
        try
        {
            return rule.Category switch
            {
                nameof(ArchitectureRuleCategory.Performance)
                    =>
                    _policy.Performance?.MaxNestedLoopDepth
                    ?? rule.Threshold,


                nameof(ArchitectureRuleCategory.Maintainability)
                    =>
                    _policy.Maintainability?.MinMaintainabilityIndex
                    ?? rule.Threshold,


                nameof(ArchitectureRuleCategory.Security)
                    =>
                    _policy.Security?.MinimumScore
                    ?? rule.Threshold,


                nameof(ArchitectureRuleCategory.Dependency)
                    =>
                    _policy.Dependency?.MaxDependencyDepth
                    ?? rule.Threshold,


                nameof(ArchitectureRuleCategory.Coupling)
                    =>
                    _policy.Coupling?.MaxCouplingRatio
                    ?? rule.Threshold,


                nameof(ArchitectureRuleCategory.Architecture)
                    =>
                    _policy.Architecture?.AllowedDependencies?.Count
                    ?? rule.Threshold,


                _ =>
                    rule.Threshold
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed resolving threshold for rule {RuleId}. Using default threshold {Threshold}.",
                rule.Id,
                rule.Threshold);


            return rule.Threshold;
        }
    }



    // ======================================================
    // ⚖️ Comparison
    // ======================================================

    private static bool IsViolation(
        ComparisonOperator operation,
        double value,
        double threshold)
    {
        return operation switch
        {
            ComparisonOperator.LessThan =>
                value < threshold,


            ComparisonOperator.GreaterThan =>
                value > threshold,


            ComparisonOperator.Equal =>
                Math.Abs(value - threshold) < 0.0001,


            ComparisonOperator.NotEqual =>
                Math.Abs(value - threshold) > 0.0001,


            _ =>
                false
        };
    }
}