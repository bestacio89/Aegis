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

    // Navigation to associated results
    public List<RuleResultEntity> RuleResults { get; set; } = new();
}
