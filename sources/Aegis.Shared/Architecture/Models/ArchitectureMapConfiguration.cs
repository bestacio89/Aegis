namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Versioned, declarative architecture classification configuration.
/// The configuration contains data only and is never executed.
/// </summary>
public sealed class ArchitectureMapConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<ArchitectureComponentMapping> Mappings { get; init; } = [];
}
