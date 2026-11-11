using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Identifies the source supporting a semantic fact. Confidence is a normalized
/// value from 0.0 to 1.0; it must not be used as a substitute for resolution status.
/// </summary>
public sealed class SemanticEvidenceProvenance
{
    public string AnalyzerId { get; init; } = string.Empty;

    public SemanticEvidenceSource Source { get; init; }

    public string? FilePath { get; init; }

    /// <summary>One-based source line, when available.</summary>
    public int? StartLine { get; init; }

    /// <summary>One-based source column, when available.</summary>
    public int? StartColumn { get; init; }

    public double Confidence { get; init; }
}
