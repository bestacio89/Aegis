using Aegis.Architecture.Bootstrap;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureEvaluatorSmokeTests
{
    [Fact]
    public async Task Every_registered_evaluator_can_analyze_an_empty_project_without_invalid_metrics()
    {
        using var directory = new TemporaryDirectory();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAegisCore();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var evaluators = scope.ServiceProvider
            .GetServices<IEvaluator>()
            .OrderBy(evaluator => evaluator.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(evaluators);
        Assert.Equal(
            evaluators.Length,
            evaluators.Select(evaluator => evaluator.GetType()).Distinct().Count());

        var context = new ProjectArchitectureContext
        {
            RootPath = directory.Path,
            ProjectName = Path.GetFileName(directory.Path),
            Language = "C#"
        };

        foreach (var evaluator in evaluators)
        {
            var results = await evaluator.EvaluateAsync(
                directory.Path,
                context,
                CancellationToken.None);

            Assert.NotNull(results);

            var materialized = results.ToArray();
            Assert.All(materialized, result => Assert.NotNull(result));

            foreach (var result in materialized)
            {
                Assert.False(string.IsNullOrWhiteSpace(result.Source));
                Assert.NotNull(result.Metrics);

                Assert.All(result.Metrics, metric =>
                {
                    Assert.False(
                        double.IsNaN(metric.Value) || double.IsInfinity(metric.Value),
                        $"{evaluator.Name} emitted non-finite metric '{metric.Key}' = {metric.Value}.");
                });
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AegisEvaluatorSmoke_{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
