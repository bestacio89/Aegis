using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Selects and executes a language-specific semantic provider while keeping
/// compiler and parser implementations behind the shared Aegis contracts.
/// </summary>
public sealed class SemanticAnalysisCoordinator
{
    private readonly IReadOnlyList<ISemanticAnalysisAdapter> _adapters;

    public SemanticAnalysisCoordinator(IEnumerable<ISemanticAnalysisAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToArray();
    }

    public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
        SemanticAnalysisRequest request,
        CancellationToken cancellationToken = default) =>
        AnalyzeCoreAsync(request, null, cancellationToken);

    public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
        SemanticAnalysisRequest request,
        SemanticAnalysisExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        return AnalyzeCoreAsync(request, executionContext, cancellationToken);
    }

    private async ValueTask<SemanticAnalysisRun> AnalyzeCoreAsync(
        SemanticAnalysisRequest request,
        SemanticAnalysisExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var languageAdapters = _adapters
            .Where(adapter => string.Equals(adapter.Language, request.Language, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (languageAdapters.Length == 0)
        {
            return CreateUnavailableRun(
                request,
                "SemanticProviderNotRegistered",
                $"No semantic provider is registered for language '{request.Language}'.");
        }

        var capableAdapters = languageAdapters
            .Where(adapter => request.RequestedCapabilities.All(capability =>
                adapter.SupportedCapabilities.Contains(capability, StringComparer.OrdinalIgnoreCase)))
            .Where(adapter => adapter.CanAnalyze(request))
            .ToArray();

        if (capableAdapters.Length == 0)
        {
            var requested = request.RequestedCapabilities.Count == 0
                ? "the requested source scope"
                : string.Join(", ", request.RequestedCapabilities);

            return CreateUnavailableRun(
                request,
                "SemanticProviderCapabilitiesUnavailable",
                $"No registered {request.Language} provider can analyse {requested} for this request.");
        }

        if (capableAdapters.Length > 1)
        {
            return CreateUnavailableRun(
                request,
                "SemanticProviderAmbiguous",
                $"Multiple {request.Language} providers can satisfy the same semantic request. Provider selection must be explicit.");
        }

        try
        {
            return executionContext is not null
                && capableAdapters[0] is ISemanticAnalysisExecutionAdapter executionAdapter
                    ? await executionAdapter.AnalyzeAsync(request, executionContext, cancellationToken).ConfigureAwait(false)
                    : await capableAdapters[0].AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = request.Language,
                EngineId = capableAdapters[0].AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Failed,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Diagnostics =
                [
                    new AnalysisDiagnostic
                    {
                        Code = "SemanticProviderFailed",
                        Component = nameof(SemanticAnalysisCoordinator),
                        Message = "The selected semantic provider failed while analysing the request.",
                        Target = request.ProjectPath,
                        ExceptionType = exception.GetType().FullName,
                        AffectsCoverage = true
                    }
                ]
            };
        }
    }

    private static SemanticAnalysisRun CreateUnavailableRun(
        SemanticAnalysisRequest request,
        string code,
        string message) =>
        new()
        {
            RunId = Guid.NewGuid().ToString("N"),
            Language = request.Language,
            EngineId = nameof(SemanticAnalysisCoordinator),
            ProjectPath = request.ProjectPath,
            Status = SemanticAnalysisStatus.Unsupported,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Diagnostics =
            [
                new AnalysisDiagnostic
                {
                    Code = code,
                    Component = nameof(SemanticAnalysisCoordinator),
                    Message = message,
                    Target = request.ProjectPath,
                    AffectsCoverage = true
                }
            ]
        };
}
