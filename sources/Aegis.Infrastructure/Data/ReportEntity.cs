using Franz.Common.Business.Domain;

namespace Aegis.Infrastructure.Data;

public class ReportEntity : Entity<int>
{
    public string ProjectName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public DateTime ScanDate { get; set; }

    public int TotalViolations { get; set; }
    public double HealthIndex { get; set; }
    public double WeightedCompliance { get; set; }
    public int FileCount { get; set; }
    public int DomainCount { get; set; }

    /// <summary>
    /// Full serialized AegisArchitectureReport snapshot (facts + rule results +
    /// metrics), captured automatically at the end of every scan. This is the
    /// only automatic output now — file exports (PDF/HTML/MD/CSV/etc.) only
    /// happen on demand, when the user picks a format/name/location in the UI.
    /// </summary>
    public string ReportJson { get; set; } = string.Empty;

    // Navigation to associated results
    public List<RuleResultEntity> RuleResults { get; set; } = new();
}