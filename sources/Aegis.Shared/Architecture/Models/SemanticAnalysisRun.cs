using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Describes one semantic-analysis attempt over a project or module. A completed
/// run means the requested analysis completed for its declared scope; partial,
/// unsupported, failed, and unrequested outcomes remain explicit.
/// </summary>
public sealed class SemanticAnalysisRun
{
    public string RunId { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public string EngineId { get; init; } = string.Empty;

    public string? EngineVersion { get; init; }

    public string ProjectPath { get; init; } = string.Empty;

    public SemanticAnalysisStatus Status { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public int AnalyzedFileCount { get; init; }

    public int SkippedFileCount { get; init; }

    /// <summary>Stable capability identifiers actually exercised by this run.</summary>
    public List<string> Capabilities { get; init; } = [];

    public List<SemanticSymbol> Symbols { get; init; } = [];

    public List<SemanticRelationship> Relationships { get; init; } = [];

    public List<AnalysisDiagnostic> Diagnostics { get; init; } = [];

    public bool IsComplete => Status == SemanticAnalysisStatus.Completed;
}
