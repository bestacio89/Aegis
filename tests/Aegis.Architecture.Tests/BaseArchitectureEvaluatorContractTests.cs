using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class BaseArchitectureEvaluatorContractTests
{
    [Fact]
    public async Task EvaluateAsync_materializes_results_once_and_normalizes_weight_and_scope()
    {
        using var directory = new TemporaryDirectory();
        var evaluator = new FixtureEvaluator();
        var enumerations = 0;

        IEnumerable<ArchitectureEvaluatorResult> DeferredResults()
        {
            enumerations++;
            yield return new ArchitectureEvaluatorResult("FixtureEvaluator", "Widget.cs")
            {
                Metrics = new Dictionary<string, double> { ["Count"] = 1 },
                Metadata = new Dictionary<string, string>
                {
                    ["FilePath"] = Path.Combine(directory.Path, "back", "Widget.cs")
                }
            };
        }

        evaluator.SetResults(DeferredResults);

        var results = await evaluator.EvaluateAsync(
            directory.Path,
            new ProjectArchitectureContext());

        Assert.Equal(1, enumerations);
        var result = Assert.Single(results);
        Assert.Equal(1, enumerations);
        Assert.Equal(1.0, result.WeightFactor);
        Assert.True(result.WasEnabled);
        Assert.Equal("File", result.Metadata["EvaluationScope"]);
        Assert.Equal(1, result.Metrics["Count"]);
    }

    [Fact]
    public async Task EvaluateAsync_preserves_explicit_scope_metadata()
    {
        using var directory = new TemporaryDirectory();
        var evaluator = new FixtureEvaluator();

        evaluator.SetResults(() =>
        [
            new ArchitectureEvaluatorResult("FixtureEvaluator", "Widget.cs")
            {
                Metadata = new Dictionary<string, string>
                {
                    ["FilePath"] = "src/Widget.cs",
                    ["EvaluationScope"] = "Project"
                }
            }
        ]);

        var result = Assert.Single(await evaluator.EvaluateAsync(
            directory.Path,
            new ProjectArchitectureContext()));

        Assert.Equal("Project", result.Metadata["EvaluationScope"]);
    }

    [Fact]
    public async Task EvaluateAsync_returns_no_results_when_disabled()
    {
        using var directory = new TemporaryDirectory();
        var evaluator = new FixtureEvaluator { IsEnabled = false };

        var results = await evaluator.EvaluateAsync(
            directory.Path,
            new ProjectArchitectureContext());

        Assert.Empty(results);
    }

    [Fact]
    public async Task EvaluateAsync_propagates_cancellation()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var evaluator = new FixtureEvaluator();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await evaluator.EvaluateAsync(
                directory.Path,
                new ProjectArchitectureContext(),
                cancellation.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EvaluateAsync_rejects_blank_project_paths(string? path)
    {
        var evaluator = new FixtureEvaluator();

        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await evaluator.EvaluateAsync(path!, new ProjectArchitectureContext()));
    }

    [Fact]
    public async Task EvaluateAsync_rejects_null_context()
    {
        var evaluator = new FixtureEvaluator();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await evaluator.EvaluateAsync(Path.GetTempPath(), null!));
    }

    private sealed class FixtureEvaluator : BaseArchitectureEvaluator
    {
        private Func<IEnumerable<ArchitectureEvaluatorResult>> _results =
            () => Array.Empty<ArchitectureEvaluatorResult>();

        public FixtureEvaluator() : base(NullLogger<FixtureEvaluator>.Instance)
        {
        }

        public override string Name => "FixtureEvaluator";

        public void SetResults(Func<IEnumerable<ArchitectureEvaluatorResult>> results) =>
            _results = results;

        protected override Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
            string projectPath,
            CancellationToken token) =>
            Task.FromResult(_results());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AegisBaseEvaluator_{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
