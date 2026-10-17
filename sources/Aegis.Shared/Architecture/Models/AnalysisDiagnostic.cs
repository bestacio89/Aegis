namespace Aegis.Shared.Architecture.Models;

public sealed class AnalysisDiagnostic
{
    public string Code { get; init; } = string.Empty;

    public string Component { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? Target { get; init; }

    public string? ExceptionType { get; init; }

    public bool AffectsCoverage { get; init; }

    public DateTimeOffset Timestamp { get; init; }
        = DateTimeOffset.UtcNow;
}