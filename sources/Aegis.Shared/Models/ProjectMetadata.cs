namespace Aegis.Shared.Models;

/// <summary>
/// Represents descriptive information about a project under analysis.
/// </summary>
public record ProjectMetadata(
    string Name,
    string Path,
    string Language,
    string Framework,
    string Version,
    DateTimeOffset LastModified
);
