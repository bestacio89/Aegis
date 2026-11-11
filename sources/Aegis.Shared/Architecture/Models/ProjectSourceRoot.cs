using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Module-owned source-root evidence. Ownership is Unknown when the build
/// metadata and path conventions do not provide sufficient evidence.
/// </summary>
public sealed record ProjectSourceRoot
{
    public string Directory { get; init; } = string.Empty;

    public ProjectSourceRootOwnership Ownership { get; init; }

    public ProjectSourceRootOrigin Origin { get; init; }

    public bool IsGenerated { get; init; }

    public bool ContainsJavaSources { get; init; }

    public string EvidenceFile { get; init; } = string.Empty;
}
