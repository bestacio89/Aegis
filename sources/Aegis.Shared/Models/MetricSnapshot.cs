namespace Aegis.Shared.Models;

/// <summary>
/// Represents a measurable metric captured during analysis.
/// </summary>
public record MetricSnapshot(
    string MetricName,
    double Value,
    string Unit,
    string SourceFile,
    DateTimeOffset CapturedAt
);
