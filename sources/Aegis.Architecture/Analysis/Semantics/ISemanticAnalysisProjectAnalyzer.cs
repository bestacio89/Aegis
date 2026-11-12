using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Performs language-specific project/module request construction and submits
/// semantic requests through the shared provider coordinator.
/// </summary>
public interface ISemanticAnalysisProjectAnalyzer
{
    string Language { get; }

    Task AnalyzeAsync(
        ProjectArchitectureContext context,
        CancellationToken cancellationToken = default);
}
