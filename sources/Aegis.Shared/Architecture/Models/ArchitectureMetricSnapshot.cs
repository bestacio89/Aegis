namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Represents a measurable metric captured during analysis.
/// </summary>
public record ArchitectureMetricSnapshot(
    string MetricName,
    double Value,
    string Unit,
    string SourceFile,
    DateTimeOffset CapturedAt
);
