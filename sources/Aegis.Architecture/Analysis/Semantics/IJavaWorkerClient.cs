using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

public interface IJavaWorkerClient
{
    bool IsConfigured { get; }

    ValueTask<SemanticAnalysisRun> AnalyzeAsync(
        SemanticAnalysisRequest request,
        CancellationToken cancellationToken = default);
}
