using Aegis.Security.RuleEngine;
using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Security.Tests;

public sealed class SecurityEvaluatorEngineTests
{
    [Theory]
    [InlineData(ProbeMode.Static, "Secrets", 1)]
    [InlineData(ProbeMode.Live, "Network", 1)]
    [InlineData(ProbeMode.Static, "Network", 0)]
    [InlineData(ProbeMode.Live, "Secrets", 0)]
    public async Task EvaluateAllAsync_runs_only_evaluators_for_the_requested_mode(
        ProbeMode mode,
        string domain,
        int expectedCalls)
    {
        var evaluator = new RecordingEvaluator(domain);
        var engine = new SecurityEvaluatorEngine(
            [evaluator],
            NullLogger<SecurityEvaluatorEngine>.Instance);

        var results = (await engine.EvaluateAllAsync(
            new ProjectSecurityContext(),
            Array.Empty<SecuritySignal>(),
            mode)).ToArray();

        Assert.Equal(expectedCalls, evaluator.CallCount);
        Assert.Equal(expectedCalls, results.Length);
    }

    [Fact]
    public async Task EvaluateAllAsync_returns_empty_when_no_evaluator_matches_mode()
    {
        var evaluator = new RecordingEvaluator("Network");
        var engine = new SecurityEvaluatorEngine(
            [evaluator],
            NullLogger<SecurityEvaluatorEngine>.Instance);

        var results = await engine.EvaluateAllAsync(
            new ProjectSecurityContext(),
            Array.Empty<SecuritySignal>(),
            ProbeMode.Static);

        Assert.Empty(results);
        Assert.Equal(0, evaluator.CallCount);
    }

    [Fact]
    public async Task EvaluateAllAsync_continues_after_an_evaluator_throws()
    {
        var failing = new ThrowingEvaluator("Secrets");
        var succeeding = new RecordingEvaluator("Application");
        var engine = new SecurityEvaluatorEngine(
            [failing, succeeding],
            NullLogger<SecurityEvaluatorEngine>.Instance);

        var results = (await engine.EvaluateAllAsync(
            new ProjectSecurityContext(),
            Array.Empty<SecuritySignal>(),
            ProbeMode.Static)).ToArray();

        Assert.Single(results);
        Assert.Equal(1, succeeding.CallCount);
        Assert.Equal(SecurityCategory.Application, results[0].Category);
    }

    [Fact]
    public async Task EvaluateAllAsync_materializes_the_signal_sequence_once_and_passes_it_to_evaluators()
    {
        var first = new RecordingEvaluator("Secrets");
        var second = new RecordingEvaluator("Application");
        var signals = new SingleEnumerationSequence<SecuritySignal>(
        [
            new SecuritySignal { Category = "Secrets", Evidence = "fixture evidence" }
        ]);
        var engine = new SecurityEvaluatorEngine(
            [first, second],
            NullLogger<SecurityEvaluatorEngine>.Instance);

        await engine.EvaluateAllAsync(
            new ProjectSecurityContext(),
            signals,
            ProbeMode.Static);

        Assert.Equal(1, signals.EnumerationCount);
        Assert.Single(first.ReceivedSignals);
        Assert.Single(second.ReceivedSignals);
        Assert.Equal("fixture evidence", first.ReceivedSignals[0].Evidence);
        Assert.Equal("fixture evidence", second.ReceivedSignals[0].Evidence);
    }

    [Fact]
    public async Task EvaluateAllAsync_forwards_the_cancellation_token_to_evaluators()
    {
        using var cancellation = new CancellationTokenSource();
        var evaluator = new RecordingEvaluator("Secrets");
        var engine = new SecurityEvaluatorEngine(
            [evaluator],
            NullLogger<SecurityEvaluatorEngine>.Instance);

        await engine.EvaluateAllAsync(
            new ProjectSecurityContext(),
            Array.Empty<SecuritySignal>(),
            ProbeMode.Static,
            cancellation.Token);

        Assert.Equal(cancellation.Token, evaluator.ReceivedCancellationToken);
    }

    private sealed class RecordingEvaluator(string domain) : ISecurityEvaluator
    {
        public string Domain { get; } = domain;
        public int CallCount { get; private set; }
        public IReadOnlyList<SecuritySignal> ReceivedSignals { get; private set; } = [];
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<SecurityEvaluationResult> EvaluateAsync(
            ProjectSecurityContext context,
            IEnumerable<SecuritySignal> signals,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ReceivedSignals = signals.ToArray();
            ReceivedCancellationToken = cancellationToken;

            return Task.FromResult(new SecurityEvaluationResult
            {
                Category = Domain switch
                {
                    "Network" => SecurityCategory.Network,
                    "Secrets" => SecurityCategory.Secrets,
                    _ => SecurityCategory.Application
                }
            });
        }
    }

    private sealed class ThrowingEvaluator(string domain) : ISecurityEvaluator
    {
        public string Domain { get; } = domain;

        public Task<SecurityEvaluationResult> EvaluateAsync(
            ProjectSecurityContext context,
            IEnumerable<SecuritySignal> signals,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic evaluator failure.");
    }

    private sealed class SingleEnumerationSequence<T>(IReadOnlyCollection<T> items) : IEnumerable<T>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("Sequence enumerated more than once.");
            }

            return items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
