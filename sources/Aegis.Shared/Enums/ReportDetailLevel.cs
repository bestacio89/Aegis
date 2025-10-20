namespace Aegis.Shared.Enums;

/// <summary>
/// Defines verbosity levels for report generation.
/// </summary>
public enum ReportDetailLevel
{
    /// <summary> Minimal summary — executive overview only (≈ 5–10 pages). </summary>
    SummaryOnly,

    /// <summary> Per-layer summaries and evaluator highlights (≈ 25–40 pages). </summary>
    Layered,

    /// <summary> Full forensic bilingual dossier (≈ 60–90 pages). </summary>
    FullForensic
}
