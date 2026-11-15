using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

public interface IKotlinWorkerClient
{
    bool IsConfigured { get; }

    ValueTask<KotlinWorkerAnalysisResult> AnalyzeAsync(
        SemanticAnalysisRequest request,
        KotlinCompilerConfiguration configuration,
        string artifactRoot,
        CancellationToken cancellationToken = default);
}
