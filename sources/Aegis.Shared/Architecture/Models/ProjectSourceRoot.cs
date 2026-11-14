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

    /// <summary>
    /// Normalized languages represented by this root. A root may contain
    /// multiple languages, for example Java and Kotlin in a mixed JVM module.
    /// </summary>
    public List<string> Languages { get; init; } = [];

    /// <summary>Compatibility flag retained for existing Java source-root consumers.</summary>
    public bool ContainsJavaSources { get; init; }

    /// <summary>True when the root is known to contain Kotlin source files.</summary>
    public bool ContainsKotlinSources => ContainsLanguage("Kotlin");

    public string EvidenceFile { get; init; } = string.Empty;

    public bool ContainsLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        return Languages.Contains(language, StringComparer.OrdinalIgnoreCase)
            || (string.Equals(language, "Java", StringComparison.OrdinalIgnoreCase) && ContainsJavaSources);
    }
}
