using Aegis.Architecture.Aggregation;
using Aegis.Architecture.RuleEngines;
using Aegis.Architecture.Scoring;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class RuleEngineOrchestrationTests
{
    [Fact]
    public async Task RunAsync_preserves_successful_facts_when_another_evaluator_fails()
    {
        using var directory = new TemporaryDirectory();
        var successful = new FixtureEvaluator(
            "HealthyEvaluator",
            (_, _) => Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            [
                new ArchitectureEvaluatorResult("HealthyEvaluator", "SampleProject")
                {
                    Category = "Architecture",
                    Metrics = new Dictionary<string, double>
                    {
                        ["CircularDependencyCount"] = 1
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["EvaluationScope"] = "Project"
                    }
                }
            ]));

        var failing = new FixtureEvaluator(
            "FailingEvaluator",
            (_, _) => throw new InvalidOperationException("intentional fixture failure"));

        var report = await CreateEngine([failing, successful]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "SampleProject",
                RootPath = directory.Path,
                Language = "C#"
            });

        Assert.Contains(report.Facts, fact => fact.Source == "HealthyEvaluator");
        Assert.Contains(report.Diagnostics, diagnostic =>
            diagnostic.Code == "EvaluatorFailed"
            && diagnostic.Component == "FailingEvaluator"
            && diagnostic.AffectsCoverage);
        Assert.Contains(report.Results, result => result.RuleId == "AEG-ARCH-R1");
        Assert.Equal(1, report.ChecksPerformed);
    }

    [Fact]
    public async Task RunAsync_applies_path_filter_before_rule_evaluation()
    {
        using var directory = new TemporaryDirectory();
        var evaluator = new FixtureEvaluator(
            "FixtureEvaluator",
            (_, _) => Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            [
                new ArchitectureEvaluatorResult("FixtureEvaluator", "FilteredProject")
                {
                    Category = "Architecture",
                    Metrics = new Dictionary<string, double>
                    {
                        ["CircularDependencyCount"] = 10
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["EvaluationScope"] = "Project"
                    }
                }
            ]));

        var report = await CreateEngine([evaluator]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "FilteredProject",
                RootPath = directory.Path,
                Language = "C#"
            },
            pathFilter: _ => false);

        Assert.Empty(report.Facts);
        Assert.Empty(report.Results);
        Assert.Equal(0, report.ChecksPerformed);
    }

    [Fact]
    public async Task RunAsync_propagates_cancellation_instead_of_returning_a_partial_report()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var evaluator = new FixtureEvaluator(
            "FixtureEvaluator",
            (_, _) => Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>([]));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CreateEngine([evaluator]).RunAsync(
                directory.Path,
                new ProjectArchitectureContext { Language = "C#" },
                cancellation.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RunAsync_rejects_blank_project_paths(string? path)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await CreateEngine([]).RunAsync(
                path!,
                new ProjectArchitectureContext()));
    }

    private static RuleEngine CreateEngine(IEnumerable<IEvaluator> evaluators)
    {
        var weighting = new RuleWeightingEngine(
            NullLogger<RuleWeightingEngine>.Instance);

        var aggregator = new CrossEvaluatorAggregator(
            NullLogger<CrossEvaluatorAggregator>.Instance,
            weighting);

        return new RuleEngine(
            evaluators,
            new RuleEngineCore(
                NullLogger<RuleEngineCore>.Instance,
                new AegisArchitecturePolicy()),
            weighting,
            aggregator,
            NullLogger<RuleEngine>.Instance);
    }

    private sealed class FixtureEvaluator : IEvaluator
    {
        private readonly Func<string, ProjectArchitectureContext,
            Task<IEnumerable<ArchitectureEvaluatorResult>>> _evaluate;

        public FixtureEvaluator(
            string name,
            Func<string, ProjectArchitectureContext,
                Task<IEnumerable<ArchitectureEvaluatorResult>>> evaluate)
        {
            Name = name;
            _evaluate = evaluate;
        }

        public string Name { get; }
        public string[] SupportedLanguages { get; } = ["*"];
        public string[] SupportedFrameworks { get; } = ["*"];
        public bool IsEnabled { get; set; } = true;
        public double WeightFactor { get; set; } = 1.0;

        public void ApplyPolicy(AegisArchitecturePolicy policy) =>
            ArgumentNullException.ThrowIfNull(policy);

        public Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateAsync(
            string projectPath,
            ProjectArchitectureContext context,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return _evaluate(projectPath, context);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AegisRuleEngine_{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
