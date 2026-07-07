using Franz.Common.Business.Domain;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Infrastructure.Data;

public class RuleResultEntity : Entity<int>
{
    public int ReportId { get; set; }

    public ReportEntity Report { get; set; } = null!;

    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;

    public ArchitectureRuleSeverity Severity { get; set; }

    public string Category { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public double ImpactScore { get; set; }
    public double WeightedImpact { get; set; }

    public DateTime DateDetected { get; set; } = DateTime.UtcNow;

    public string? Domain { get; set; }
    public string? AnalyzerVersion { get; set; }
    public string? Project { get; set; }
}