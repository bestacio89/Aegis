using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Represents the result of a single evaluator execution.
/// 
/// Evaluators produce deterministic findings based on
/// ProjectArchitectureContext. Rule engines consume these
/// results to produce violations, scores and recommendations.
/// </summary>
public sealed class ArchitectureEvaluatorResult
{
    // ===============================================================
    // Identification
    // ===============================================================

    /// <summary>
    /// Evaluator that produced this result.
    /// Example: DependencyEvaluator
    /// </summary>
    public string Source { get; set; } = string.Empty;


    /// <summary>
    /// Evaluated target.
    /// Usually a file, module, component or dependency.
    /// </summary>
    public string Target { get; set; } = string.Empty;



    // ===============================================================
    // Context
    // ===============================================================

    public string? Domain { get; set; }

    public string? Category { get; set; }


    /// <summary>
    /// Project context identifier.
    /// </summary>
    public string? ProjectName { get; set; }


    /// <summary>
    /// Detected language when evaluation happened.
    /// </summary>
    public string? Language { get; set; }


    /// <summary>
    /// Detected framework when evaluation happened.
    /// </summary>
    public string? Framework { get; set; }


    /// <summary>
    /// Logical architecture layer.
    /// </summary>
    public string? Layer { get; set; }


    /// <summary>
    /// Module/package involved.
    /// </summary>
    public string? Module { get; set; }



    // ===============================================================
    // Architecture Relations
    // ===============================================================

    /// <summary>
    /// Dependency information when result comes from
    /// dependency/boundary analysis.
    /// </summary>
    public ArchitectureDependencyContext? Dependency { get; set; }


    /// <summary>
    /// Pattern involved in the finding.
    /// Example: CQRS, Repository, Mediator.
    /// </summary>
    public string? RelatedPattern { get; set; }


    /// <summary>
    /// Boundary involved in the finding.
    /// </summary>
    public ArchitectureBoundaryContext? RelatedBoundary { get; set; }



    // ===============================================================
    // Evidence
    // ===============================================================

    /// <summary>
    /// File where evidence was found.
    /// </summary>
    public string? File { get; set; }


    /// <summary>
    /// Line number when available.
    /// </summary>
    public int? LineNumber { get; set; }


    /// <summary>
    /// Code fragment or evidence identifier.
    /// </summary>
    public string? Evidence { get; set; }



    // ===============================================================
    // Raw Metrics
    // ===============================================================

    public Dictionary<string, double> Metrics { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);


    public Dictionary<string, string> Metadata { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);



    // ===============================================================
    // Rule Engine
    // ===============================================================

    public List<ArchitectureRuleresult> RuleResults { get; set; }
        = new();


    public double WeightFactor { get; set; } = 1.0;


    public bool WasEnabled { get; set; } = true;



    // ===============================================================
    // Scoring
    // ===============================================================

    public double DomainWeightedCompliance { get; set; }

    public double DomainMaintainabilityIndex { get; set; }

    public double DomainHealthIndex { get; set; }

    public double DomainResilienceIndex { get; set; }



    // ===============================================================
    // Traceability
    // ===============================================================

    public string? PolicyVersion { get; set; }

    public string? RuleSetVersion { get; set; }

    public string? AegisEngineVersion { get; set; }


    /// <summary>
    /// Confidence inherited from detector context.
    /// </summary>
    public double DetectionConfidence { get; set; }



    // ===============================================================
    // Intelligence Layer
    // ===============================================================

    public string? Summary { get; set; }


    public List<string> Recommendations { get; set; }
        = new();


    public List<string> Anomalies { get; set; }
        = new();



    public bool ContainsCriticalViolations =>
        RuleResults.Any(
            r => r.Severity >= ArchitectureRuleSeverity.Critical);



    // ===============================================================
    // Constructors
    // ===============================================================

    public ArchitectureEvaluatorResult()
    {
    }



    public ArchitectureEvaluatorResult(
        string source,
        string target,
        string? domain = null)
    {
        Source = source;
        Target = target;
        Domain = domain ?? "General";
    }



    // ===============================================================
    // Helpers
    // ===============================================================

    public void AddMetric(
        string name,
        double value)
    {
        Metrics[name] = value;
    }



    public void AddRuleResult(
        ArchitectureRuleresult rule)
    {
        RuleResults.Add(rule);

        if (rule.Severity == ArchitectureRuleSeverity.Blocker)
        {
            Anomalies.Add(
                $"Blocker rule triggered: {rule.RuleId} ({rule.RuleName})");
        }
    }



    public override string ToString()
    {
        var metricsSummary =
            Metrics.Any()
                ? string.Join(
                    ", ",
                    Metrics.Select(
                        x => $"{x.Key}={x.Value:0.##}"))
                : "No metrics";


        return
            $"{Source} → {Target} " +
            $"[{Domain}] " +
            $"Weight={WeightFactor:0.##} | " +
            $"{metricsSummary} | " +
            $"{RuleResults.Count} rules evaluated";
    }
}