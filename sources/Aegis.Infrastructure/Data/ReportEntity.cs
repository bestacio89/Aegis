using Franz.Common.Business.Domain;

namespace Aegis.Infrastructure.Data;

public sealed class ReportEntity : Entity<int>
{
    // ===============================================================
    // Identity
    // ===============================================================

    public Guid ScanId { get; set; }



    // ===============================================================
    // Project Information
    // ===============================================================

    public string ProjectName { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;


    public string Language { get; set; } = string.Empty;

    public string Framework { get; set; } = string.Empty;



    // ===============================================================
    // Scan Metadata
    // ===============================================================

    public DateTime ScanDate { get; set; }
        = DateTime.UtcNow;


    public string? AnalyzerVersion { get; set; }

    public string? RuleSetVersion { get; set; }



    // ===============================================================
    // Detection Context
    // ===============================================================

    public string? ArchitectureStyle { get; set; }

    public string? DomainType { get; set; }

    public string? DetectionStrategy { get; set; }


    public double DetectionConfidence { get; set; }



    // ===============================================================
    // Aggregated Metrics
    // ===============================================================

    public int FileCount { get; set; }

    public int FactCount { get; set; }

    public int TotalViolations { get; set; }

    public int DomainCount { get; set; }



    public double HealthIndex { get; set; }

    public double WeightedCompliance { get; set; }

    public double MaintainabilityIndex { get; set; }

    public double ResilienceIndex { get; set; }



    // ===============================================================
    // Snapshot Storage
    // ===============================================================

    /// <summary>
    /// Complete serialized AegisArchitectureReport.
    ///
    /// Contains:
    /// - Facts
    /// - Rule results
    /// - Domains
    /// - Metrics
    /// - Compliance scores
    ///
    /// Used for reconstruction, AI analysis,
    /// and historical comparison.
    /// </summary>
    public string ReportJson { get; set; } = string.Empty;



    /// <summary>
    /// Serialized ProjectArchitectureContext snapshot.
    /// Keeps the detected architecture state.
    /// </summary>
    public string? ContextJson { get; set; }



    /// <summary>
    /// Policy snapshot used during execution.
    /// Allows reproducing the same scoring later.
    /// </summary>
    public string? PolicyJson { get; set; }



    // ===============================================================
    // Navigation
    // ===============================================================

    public List<RuleResultEntity> RuleResults { get; set; }
        = new();
}