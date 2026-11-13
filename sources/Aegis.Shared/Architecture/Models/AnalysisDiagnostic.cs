namespace Aegis.Shared.Architecture.Models;

public sealed class AnalysisDiagnostic
{
    public string Code { get; init; } = string.Empty;

    public string Component { get; init; } = string.Empty;

    /// <summary>Originating analysis subsystem, such as discovery, semantic analysis, or an evaluator.</summary>
    public string? Source { get; init; }

    /// <summary>Language associated with semantic diagnostics.</summary>
    public string? Language { get; init; }

    /// <summary>Semantic engine that produced the diagnostic.</summary>
    public string? EngineId { get; init; }

    /// <summary>Identifier of the semantic-analysis run that produced this diagnostic.</summary>
    public string? RunId { get; init; }

    /// <summary>Project or module path analyzed by the originating run.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>Provider-reported severity, when available.</summary>
    public string? Severity { get; init; }

    /// <summary>One-based source line reported by the analyzer, when available.</summary>
    public int? Line { get; init; }

    /// <summary>One-based source column reported by the analyzer, when available.</summary>
    public int? Column { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Target { get; init; }

    public string? ExceptionType { get; init; }

    public bool AffectsCoverage { get; init; }

    public DateTimeOffset Timestamp { get; init; }
        = DateTimeOffset.UtcNow;
}