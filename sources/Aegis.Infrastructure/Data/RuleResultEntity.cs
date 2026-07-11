using Franz.Common.Business.Domain;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Infrastructure.Data;

public sealed class RuleResultEntity : Entity<int>
{
    public int ReportId { get; set; }

    public ReportEntity Report { get; set; } = null!;



    // ===============================================================
    // Scan Traceability
    // ===============================================================

    public Guid ScanId { get; set; }



    // ===============================================================
    // Rule Identity
    // ===============================================================

    public string RuleId { get; set; } = string.Empty;

    public string RuleName { get; set; } = string.Empty;

    public string? RuleVersion { get; set; }



    // ===============================================================
    // Evaluation Origin
    // ===============================================================

    /// <summary>
    /// Evaluator that produced the fact consumed by the rule.
    /// Example:
    /// ComplexityEvaluator
    /// SecurityEvaluator
    /// CouplingEvaluator
    /// </summary>
    public string EvaluatorName { get; set; } = string.Empty;



    public string? AnalyzerVersion { get; set; }



    // ===============================================================
    // Classification
    // ===============================================================

    public ArchitectureRuleSeverity Severity { get; set; }

    public string Category { get; set; } = string.Empty;

    public string? Domain { get; set; }



    // ===============================================================
    // Location / Target
    // ===============================================================

    public string Target { get; set; } = string.Empty;

    public string? SourceFile { get; set; }

    public string? SourceLayer { get; set; }

    public string? SourceModule { get; set; }



    // ===============================================================
    // Rule Result
    // ===============================================================

    public string Message { get; set; } = string.Empty;

    public bool IsCompliant { get; set; }



    /// <summary>
    /// Raw evaluator evidence serialized as JSON.
    /// Keeps the original metrics that triggered the rule.
    /// </summary>
    public string? EvidenceJson { get; set; }



    // ===============================================================
    // Scoring
    // ===============================================================

    public double ImpactScore { get; set; }

    public double WeightFactor { get; set; } = 1.0;

    public double WeightedImpact { get; set; }



    // ===============================================================
    // Metadata
    // ===============================================================

    public DateTime DateDetected { get; set; }
        = DateTime.UtcNow;


    public string? Project { get; set; }
}