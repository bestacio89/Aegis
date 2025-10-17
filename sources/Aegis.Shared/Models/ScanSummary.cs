using Aegis.Shared.Enums;

namespace Aegis.Shared.Models;

/// <summary>
/// Represents a compact summary of a completed scan session.
/// </summary>
public record ScanSummary(
    string ProjectName,
    string Language,
    int FilesScanned,
    int RulesEvaluated,
    int ViolationsFound,
    ScanStatus Status,
    DateTimeOffset Timestamp
);
