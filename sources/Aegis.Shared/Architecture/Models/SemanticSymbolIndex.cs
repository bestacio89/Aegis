using System.Collections.ObjectModel;
using System.IO;
using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Immutable, snapshot-scoped index of every semantic declaration occurrence.
/// It preserves duplicates and incomplete ownership rather than resolving
/// ambiguity or applying run-status eligibility policy.
/// </summary>
public sealed class SemanticSymbolIndex
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<SemanticSymbolOccurrence>> _byRun;
    private readonly IReadOnlyDictionary<SemanticSymbolIdentity, IReadOnlyList<SemanticSymbolOccurrence>> _byIdentity;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<SemanticSymbolOccurrence>> _byProviderId;
    private readonly IReadOnlyDictionary<QualifiedNameKey, IReadOnlyList<SemanticSymbolOccurrence>> _byQualifiedName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<SemanticSymbolOccurrence>> _byOwnerProjectPath;

    private SemanticSymbolIndex(
        IReadOnlyList<SemanticSymbolOccurrence> occurrences,
        IReadOnlyDictionary<int, IReadOnlyList<SemanticSymbolOccurrence>> byRun,
        IReadOnlyDictionary<SemanticSymbolIdentity, IReadOnlyList<SemanticSymbolOccurrence>> byIdentity,
        IReadOnlyDictionary<string, IReadOnlyList<SemanticSymbolOccurrence>> byProviderId,
        IReadOnlyDictionary<QualifiedNameKey, IReadOnlyList<SemanticSymbolOccurrence>> byQualifiedName,
        IReadOnlyDictionary<string, IReadOnlyList<SemanticSymbolOccurrence>> byOwnerProjectPath)
    {
        Occurrences = occurrences;
        _byRun = byRun;
        _byIdentity = byIdentity;
        _byProviderId = byProviderId;
        _byQualifiedName = byQualifiedName;
        _byOwnerProjectPath = byOwnerProjectPath;
    }

    public IReadOnlyList<SemanticSymbolOccurrence> Occurrences { get; }

    public int Count => Occurrences.Count;

    public static SemanticSymbolIndex Create(IEnumerable<SemanticAnalysisRun> semanticRuns)
    {
        ArgumentNullException.ThrowIfNull(semanticRuns);

        var occurrences = new List<SemanticSymbolOccurrence>();
        var runGroups = new Dictionary<int, IReadOnlyList<SemanticSymbolOccurrence>>();
        var runIndex = 0;

        foreach (var run in semanticRuns)
        {
            ArgumentNullException.ThrowIfNull(run);
            var runOccurrences = new List<SemanticSymbolOccurrence>();
            var symbols = run.Symbols ?? [];

            for (var symbolIndex = 0; symbolIndex < symbols.Count; symbolIndex++)
            {
                var symbol = symbols[symbolIndex];
                if (symbol is null)
                {
                    throw new InvalidOperationException(
                        $"Semantic run at index {runIndex} contains a null symbol at index {symbolIndex}.");
                }

                SemanticSymbolIdentity.TryCreate(run, symbol, out var identity);
                var occurrence = new SemanticSymbolOccurrence(
                    runIndex,
                    symbolIndex,
                    run,
                    symbol,
                    identity,
                    ResolveOwnerProjectPath(run, symbol));

                occurrences.Add(occurrence);
                runOccurrences.Add(occurrence);
            }

            runGroups.Add(runIndex, Array.AsReadOnly(runOccurrences.ToArray()));
            runIndex++;
        }

        var occurrenceArray = occurrences.ToArray();
        return new SemanticSymbolIndex(
            Array.AsReadOnly(occurrenceArray),
            Freeze(runGroups),
            FreezeGroup(occurrenceArray.Where(item => item.Identity is not null), item => item.Identity!, SemanticSymbolIdentity.Comparer),
            FreezeGroup(occurrenceArray.Where(item => !string.IsNullOrWhiteSpace(item.Symbol.Id)), item => item.Symbol.Id, StringComparer.Ordinal),
            FreezeGroup(
                occurrenceArray.Where(item => !string.IsNullOrWhiteSpace(item.Symbol.QualifiedName)),
                item => new QualifiedNameKey(item.Language, item.Symbol.QualifiedName),
                QualifiedNameKeyComparer.Instance),
            FreezeGroup(
                occurrenceArray.Where(item => !string.IsNullOrWhiteSpace(item.OwnerProjectPath)),
                item => item.OwnerProjectPath!,
                PathComparer));
    }

    public IReadOnlyList<SemanticSymbolOccurrence> ForRun(int runIndex) =>
        _byRun.TryGetValue(runIndex, out var occurrences)
            ? occurrences
            : Array.Empty<SemanticSymbolOccurrence>();

    public IReadOnlyList<SemanticSymbolOccurrence> FindByIdentity(SemanticSymbolIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return _byIdentity.TryGetValue(identity, out var occurrences)
            ? occurrences
            : Array.Empty<SemanticSymbolOccurrence>();
    }

    public IReadOnlyList<SemanticSymbolOccurrence> FindByProviderId(string providerSymbolId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSymbolId);
        return _byProviderId.TryGetValue(providerSymbolId, out var occurrences)
            ? occurrences
            : Array.Empty<SemanticSymbolOccurrence>();
    }

    public IReadOnlyList<SemanticSymbolOccurrence> FindByQualifiedName(string language, string qualifiedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedName);
        return _byQualifiedName.TryGetValue(new QualifiedNameKey(language, qualifiedName), out var occurrences)
            ? occurrences
            : Array.Empty<SemanticSymbolOccurrence>();
    }

    public IReadOnlyList<SemanticSymbolOccurrence> FindByOwnerProjectPath(string ownerProjectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerProjectPath);
        if (!TryNormalizePath(ownerProjectPath, out var normalizedPath))
            return Array.Empty<SemanticSymbolOccurrence>();

        return _byOwnerProjectPath.TryGetValue(normalizedPath, out var occurrences)
            ? occurrences
            : Array.Empty<SemanticSymbolOccurrence>();
    }

    private static string? ResolveOwnerProjectPath(SemanticAnalysisRun run, SemanticSymbol symbol)
    {
        var owner = symbol.Origin is SemanticSymbolOrigin.Source or SemanticSymbolOrigin.Generated
            ? run.ProjectPath
            : symbol.OwningProjectPath;

        return TryNormalizePath(owner, out var normalizedPath)
            ? normalizedPath
            : null;
    }

    private static bool TryNormalizePath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return false;

        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<SemanticSymbolOccurrence>> Freeze(
        Dictionary<int, IReadOnlyList<SemanticSymbolOccurrence>> source) =>
        new ReadOnlyDictionary<int, IReadOnlyList<SemanticSymbolOccurrence>>(source);

    private static IReadOnlyDictionary<TKey, IReadOnlyList<SemanticSymbolOccurrence>> FreezeGroup<TKey>(
        IEnumerable<SemanticSymbolOccurrence> source,
        Func<SemanticSymbolOccurrence, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var groups = source
            .GroupBy(keySelector, comparer)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SemanticSymbolOccurrence>)Array.AsReadOnly(group.ToArray()),
                comparer);

        return new ReadOnlyDictionary<TKey, IReadOnlyList<SemanticSymbolOccurrence>>(groups);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record QualifiedNameKey(string Language, string QualifiedName);

    private sealed class QualifiedNameKeyComparer : IEqualityComparer<QualifiedNameKey>
    {
        public static QualifiedNameKeyComparer Instance { get; } = new();

        public bool Equals(QualifiedNameKey? x, QualifiedNameKey? y) =>
            ReferenceEquals(x, y)
            || x is not null && y is not null
                && StringComparer.OrdinalIgnoreCase.Equals(x.Language, y.Language)
                && StringComparer.Ordinal.Equals(x.QualifiedName, y.QualifiedName);

        public int GetHashCode(QualifiedNameKey key) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Language),
                StringComparer.Ordinal.GetHashCode(key.QualifiedName));
    }
}
