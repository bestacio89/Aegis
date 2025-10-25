using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Defines deterministic quantitative thresholds and metadata for each security severity.
/// </summary>
public static class SecuritySeverityThresholds
{
    public static (double Min, double Max, string Description) GetRange(SecuritySeverity severity) =>
        severity switch
        {
            SecuritySeverity.Info => (0.0, 1.9, "Informational or best-practice notice."),
            SecuritySeverity.Low => (2.0, 3.9, "Low impact, minor misconfiguration or limited exploit."),
            SecuritySeverity.Medium => (4.0, 6.9, "Moderate impact; exploit possible under specific conditions."),
            SecuritySeverity.High => (7.0, 8.9, "Severe impact; directly exploitable with tangible risk."),
            SecuritySeverity.Critical => (9.0, 10.0, "Catastrophic impact; high probability of full compromise."),
            _ => (0.0, 0.0, "Unknown or undefined severity.")
        };

    public static double GetDefaultThreshold(SecuritySeverity severity) =>
        severity switch
        {
            SecuritySeverity.Info => 0.0,
            SecuritySeverity.Low => 2.0,
            SecuritySeverity.Medium => 4.0,
            SecuritySeverity.High => 7.0,
            SecuritySeverity.Critical => 9.0,
            _ => 0.0
        };

    public static string GetColor(SecuritySeverity severity) =>
        severity switch
        {
            SecuritySeverity.Info => "#00BFFF",      // cyan-blue
            SecuritySeverity.Low => "#32CD32",       // green
            SecuritySeverity.Medium => "#FFD700",    // gold
            SecuritySeverity.High => "#FF8C00",      // orange
            SecuritySeverity.Critical => "#FF0000",  // red
            _ => "#808080"
        };
}
