using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Language-specific semantic analysis boundary. Implementations translate
/// language/compiler evidence into Aegis.Shared contracts; parser and compiler
/// types must not escape this boundary.
/// </summary>
public interface ISemanticAnalysisAdapter
{
    string AdapterId { get; }

    string Language { get; }

    /// <summary>Stable capability identifiers supported by this adapter.</summary>
    IReadOnlyCollection<string> SupportedCapabilities { get; }

    bool CanAnalyze(SemanticAnalysisRequest request);

    ValueTask<SemanticAnalysisRun> AnalyzeAsync(
        SemanticAnalysisRequest request,
        CancellationToken cancellationToken = default);
}
