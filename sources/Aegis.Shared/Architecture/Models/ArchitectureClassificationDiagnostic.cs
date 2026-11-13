namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Describes a classification problem or uncertainty without converting it
/// into an architectural dependency violation.
/// </summary>
public sealed class ArchitectureClassificationDiagnostic
{
    public string Code { get; init; } = string.Empty;

    public string Severity { get; init; } = "Warning";

    public string Message { get; init; } = string.Empty;

    public string? Target { get; init; }

    public string? CodeComponentId { get; init; }

    public string? MappingId { get; init; }

    public string? Language { get; init; }

    public string? ModulePath { get; init; }

    /// <summary>True when this diagnostic reduces the completeness of classification coverage.</summary>
    public bool AffectsCoverage { get; init; } = true;
}
