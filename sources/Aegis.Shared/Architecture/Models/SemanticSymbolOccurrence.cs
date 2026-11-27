namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// One declaration occurrence retained from a semantic run. The same declaration
/// may occur in multiple runs; run and symbol indexes preserve that provenance.
/// </summary>
public sealed record SemanticSymbolOccurrence(
    int RunIndex,
    int SymbolIndex,
    SemanticAnalysisRun Run,
    SemanticSymbol Symbol,
    SemanticSymbolIdentity? Identity,
    string? OwnerProjectPath)
{
    public string Language => string.IsNullOrWhiteSpace(Symbol.Language)
        ? Run.Language
        : Symbol.Language;
}
