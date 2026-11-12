using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class SemanticAnalysisPipelineTests
{
    [Fact]
    public async Task AnalyzeAsync_executes_registered_language_analyzers_in_deterministic_order()
    {
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
        [
            new RecordingAnalyzer("Python", executionOrder),
            new RecordingAnalyzer("Java", executionOrder),
            new RecordingAnalyzer("C#", executionOrder)
        ],
        NullLogger<SemanticAnalysisPipeline>.Instance);

        var context = new ProjectArchitectureContext
        {
            ProjectName = "Polyglot",
            RootPath = Path.GetTempPath()
        };

        await pipeline.AnalyzeAsync(context);

        Assert.Equal(["C#", "Java", "Python"], executionOrder);
        Assert.Equal(3, context.SemanticAnalysisRuns.Count);
        Assert.All(context.SemanticAnalysisRuns, run =>
            Assert.Equal(SemanticAnalysisStatus.Completed, run.Status));
    }

    [Fact]
    public async Task AnalyzeAsync_isolates_provider_failures_and_continues_with_other_languages()
    {
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
        [
            new RecordingAnalyzer("Python", executionOrder),
            new RecordingAnalyzer("Java", executionOrder, shouldFail: true)
        ],
        NullLogger<SemanticAnalysisPipeline>.Instance);

        var context = new ProjectArchitectureContext
        {
            ProjectName = "Polyglot",
            RootPath = Path.GetTempPath()
        };

        await pipeline.AnalyzeAsync(context);

        Assert.Equal(["Java", "Python"], executionOrder);
        Assert.Equal(2, context.SemanticAnalysisRuns.Count);

        var failedRun = Assert.Single(context.SemanticAnalysisRuns, run =>
            run.Language == "Java");
        Assert.Equal(SemanticAnalysisStatus.Failed, failedRun.Status);
        Assert.Contains(failedRun.Diagnostics, diagnostic =>
            diagnostic.Code == "SemanticProjectAnalysisFailed"
            && diagnostic.AffectsCoverage);

        var completedRun = Assert.Single(context.SemanticAnalysisRuns, run =>
            run.Language == "Python");
        Assert.Equal(SemanticAnalysisStatus.Completed, completedRun.Status);
    }

    [Fact]
    public async Task AnalyzeAsync_propagates_cancellation_before_starting_a_provider()
    {
        var executionOrder = new List<string>();
        var pipeline = new SemanticAnalysisPipeline(
            [new RecordingAnalyzer("Java", executionOrder)],
            NullLogger<SemanticAnalysisPipeline>.Instance);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.AnalyzeAsync(
                new ProjectArchitectureContext
                {
                    ProjectName = "Polyglot",
                    RootPath = Path.GetTempPath()
                },
                cancellation.Token));

        Assert.Empty(executionOrder);
    }

    private sealed class RecordingAnalyzer(
        string language,
        ICollection<string> executionOrder,
        bool shouldFail = false) : ISemanticAnalysisProjectAnalyzer
    {
        public string Language => language;

        public Task AnalyzeAsync(
            ProjectArchitectureContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            executionOrder.Add(language);

            if (shouldFail)
                throw new InvalidOperationException($"Failure from {language} provider.");

            context.SemanticAnalysisRuns.Add(new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = language,
                EngineId = $"{language}-test-provider",
                ProjectPath = context.RootPath,
                Status = SemanticAnalysisStatus.Completed,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });

            return Task.CompletedTask;
        }
    }
}
