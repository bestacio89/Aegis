using System.IO;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Canonical identity for a semantic declaration. Provider symbol IDs are opaque
/// and are only unique within the owning project and language.
/// </summary>
public sealed class SemanticSymbolIdentity : IEquatable<SemanticSymbolIdentity>
{
    private static readonly StringComparer ProjectPathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private SemanticSymbolIdentity(string language, string ownerProjectPath, string symbolId)
    {
        Language = language;
        OwnerProjectPath = ownerProjectPath;
        SymbolId = symbolId;
    }

    public string Language { get; }

    public string OwnerProjectPath { get; }

    public string SymbolId { get; }

    /// <summary>
    /// Comparer for canonical identities. Language is case-insensitive, provider
    /// symbol IDs are ordinal, and project paths follow host filesystem semantics.
    /// </summary>
    public static IEqualityComparer<SemanticSymbolIdentity> Comparer { get; } =
        new IdentityComparer();

    public static SemanticSymbolIdentity Create(
        string language,
        string ownerProjectPath,
        string symbolId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolId);
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownerProjectPath));
        return new SemanticSymbolIdentity(language.Trim(), normalizedPath, symbolId);
    }

    /// <summary>
    /// Creates an identity only when ownership is defensible. Source and generated
    /// declarations belong to the project being analyzed. External declarations
    /// require provider-supplied ownership; their analysis-context ProjectPath is
    /// deliberately ignored.
    /// </summary>
    public static bool TryCreate(
        SemanticAnalysisRun run,
        SemanticSymbol symbol,
        out SemanticSymbolIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(symbol);

        identity = null;
        if (string.IsNullOrWhiteSpace(symbol.Id))
            return false;

        var ownerProjectPath = symbol.Origin is SemanticSymbolOrigin.Source or SemanticSymbolOrigin.Generated
            ? run.ProjectPath
            : symbol.OwningProjectPath;
        if (string.IsNullOrWhiteSpace(ownerProjectPath))
            return false;

        if (symbol.Origin is not (SemanticSymbolOrigin.Source or SemanticSymbolOrigin.Generated)
            && !Path.IsPathFullyQualified(ownerProjectPath))
        {
            return false;
        }

        var language = string.IsNullOrWhiteSpace(symbol.Language)
            ? run.Language
            : symbol.Language;
        if (string.IsNullOrWhiteSpace(language))
            return false;

        try
        {
            identity = Create(language, ownerProjectPath, symbol.Id);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    public bool Equals(SemanticSymbolIdentity? other) =>
        other is not null
        && StringComparer.OrdinalIgnoreCase.Equals(Language, other.Language)
        && ProjectPathComparer.Equals(OwnerProjectPath, other.OwnerProjectPath)
        && StringComparer.Ordinal.Equals(SymbolId, other.SymbolId);

    public override bool Equals(object? obj) =>
        obj is SemanticSymbolIdentity other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Language),
            ProjectPathComparer.GetHashCode(OwnerProjectPath),
            StringComparer.Ordinal.GetHashCode(SymbolId));

    private sealed class IdentityComparer : IEqualityComparer<SemanticSymbolIdentity>
    {
        public bool Equals(SemanticSymbolIdentity? x, SemanticSymbolIdentity? y) =>
            ReferenceEquals(x, y)
            || x is not null && x.Equals(y);

        public int GetHashCode(SemanticSymbolIdentity identity) =>
            identity.GetHashCode();
    }
}
