namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Describes semantic dependency evidence omitted from the logical architecture
/// graph because one or both code containers lack a unique logical membership.
/// </summary>
public sealed class ArchitecturalDependencyProjectionDiagnostic
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string Scope { get; init; } = "Production";

    public string SourceCodeComponentId { get; init; } = string.Empty;

    public string TargetCodeComponentId { get; init; } = string.Empty;

    public int RelationshipCount { get; init; }

    public bool AffectsCoverage { get; init; } = true;
}
