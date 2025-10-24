using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Represents a compact summary of a completed scan session.
/// </summary>
public record ArchitectureScanSummary(
    string ProjectName,
    string Language,
    int FilesScanned,
    int RulesEvaluated,
    int ViolationsFound,
    ArchitectureScanStatus Status,
    DateTimeOffset Timestamp
);
