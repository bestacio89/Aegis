using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Performs language-specific project/module request construction and submits
/// semantic requests through the shared provider coordinator.
/// </summary>
public interface ISemanticAnalysisProjectAnalyzer
{
    string Language { get; }

    /// <summary>Lower values execute first; JVM Kotlin precedes Java for source-level interop.</summary>
    int ExecutionOrder => 0;

    Task AnalyzeAsync(
        ProjectArchitectureContext context,
        CancellationToken cancellationToken = default);

    Task AnalyzeAsync(
        ProjectArchitectureContext context,
        SemanticAnalysisExecutionContext executionContext,
        CancellationToken cancellationToken = default) =>
        AnalyzeAsync(context, cancellationToken);
}
