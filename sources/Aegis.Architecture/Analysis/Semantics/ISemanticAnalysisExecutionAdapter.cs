using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Optional adapter contract for providers that produce transient compiler
/// artifacts needed by later language analyzers in the same pipeline run.
/// </summary>
public interface ISemanticAnalysisExecutionAdapter : ISemanticAnalysisAdapter
{
    ValueTask<SemanticAnalysisRun> AnalyzeAsync(
        SemanticAnalysisRequest request,
        SemanticAnalysisExecutionContext executionContext,
        CancellationToken cancellationToken = default);
}
