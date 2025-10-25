namespace Aegis.Shared.Security.Models;

/// <summary>
/// Minimal DTO for charts/tables (e.g., OxyPlot bars, WPF grids).
/// </summary>
public sealed class SecuritySummaryDto
{
    public string Key { get; init; } = string.Empty;   // e.g., Category or Domain
    public int Count { get; init; }                    // e.g., number of findings
    public string? Extra { get; init; }                // optional label (e.g., risk, layer)
}
