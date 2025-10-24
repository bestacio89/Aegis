namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Represents descriptive information about a project under analysis.
/// </summary>
public record ProjectArchitectureMetadata(
    string Name,
    string Path,
    string Language,
    string Framework,
    string Version,
    DateTimeOffset LastModified
);
