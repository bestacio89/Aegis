namespace Aegis.App.Wpf.Models;

public sealed record DashboardMetrics
(
    string ProjectName,
    int FilesScanned,
    int TotalViolations,
    double ComplianceScore,
    double ArchitectureConfidence,
    string Language,
    string Framework,
    string ArchitectureStyle,
    string Layer,
    DateTimeOffset ScanDate
);