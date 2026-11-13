namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Local artifact evidence for one module. Direct cached artifacts can be used
/// for binding, but the result is incomplete until a full transitive graph is known.
/// </summary>
public sealed record JvmClasspathResolution(
    IReadOnlyList<string> Entries,
    IReadOnlyList<string> UnresolvedDependencies,
    bool HasDeclaredDependencies,
    bool IsComplete);
