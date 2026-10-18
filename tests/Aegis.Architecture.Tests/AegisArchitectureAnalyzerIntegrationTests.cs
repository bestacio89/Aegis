using Aegis.Architecture.Aggregation;
using Aegis.Architecture.Evaluators;
using Aegis.Architecture.RuleEngines;
using Aegis.Architecture.Scoring;
using Aegis.Architecture.Services;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class AegisArchitectureAnalyzerIntegrationTests
{
    [Fact]
    public async Task AnalyzeAsync_runs_context_detection_evaluation_rules_and_report_enrichment()
    {
        using var project = new TemporaryProject();
        project.Write("Fixture.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        project.Write("Program.cs", """
            public static class Program
            {
                public static void Main() { }
            }
            """);

        var weighting = new RuleWeightingEngine(
            NullLogger<RuleWeightingEngine>.Instance);
        var aggregator = new CrossEvaluatorAggregator(
            NullLogger<CrossEvaluatorAggregator>.Instance,
            weighting);
        var core = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());
        var engine = new RuleEngine(
            [new CircularDependencyFixtureEvaluator()],
            core,
            weighting,
            aggregator,
            NullLogger<RuleEngine>.Instance);
        var analyzer = new AegisArchitectureAnalyzer(
            engine,
            NullLogger<AegisArchitectureAnalyzer>.Instance);

        var report = await analyzer.AnalyzeAsync(project.Root);

        Assert.Equal(Path.GetFileName(project.Root), report.ProjectName);
        Assert.Equal(project.Root, report.ProjectPath);
        Assert.Equal("C#", report.Language);
        Assert.True(report.TotalFilesScanned >= 1);

        Assert.Contains(report.Facts, fact =>
            fact.Source == "CircularDependencyEvaluator"
            && fact.Metrics.GetValueOrDefault("CircularDependencyCount") == 1);
        Assert.Contains(report.Facts, fact =>
            fact.Source == "ProjectContextFact"
            && fact.Metadata.GetValueOrDefault("Language") == "C#");

        var violation = Assert.Single(
            report.Results,
            result => result.RuleId == "AEG-ARCH-R1");
        Assert.False(violation.IsCompliant);

        Assert.NotNull(report.EvaluationStats);
        Assert.True(report.ChecksPerformed >= 1);
        Assert.True(report.EvaluationsViolated >= 1);
        Assert.True(double.IsFinite(report.Metrics.ProjectHealthIndex));
    }

    [Fact]
    public async Task AnalyzeAsync_honors_pre_cancelled_tokens_before_scanning_the_project()
    {
        using var project = new TemporaryProject();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var weighting = new RuleWeightingEngine(
            NullLogger<RuleWeightingEngine>.Instance);
        var aggregator = new CrossEvaluatorAggregator(
            NullLogger<CrossEvaluatorAggregator>.Instance,
            weighting);
        var engine = new RuleEngine(
            Array.Empty<IEvaluator>(),
            new RuleEngineCore(
                NullLogger<RuleEngineCore>.Instance,
                new AegisArchitecturePolicy()),
            weighting,
            aggregator,
            NullLogger<RuleEngine>.Instance);
        var analyzer = new AegisArchitectureAnalyzer(
            engine,
            NullLogger<AegisArchitectureAnalyzer>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            analyzer.AnalyzeAsync(project.Root, cancellation.Token));
    }

    private sealed class CircularDependencyFixtureEvaluator : BaseArchitectureEvaluator
    {
        public CircularDependencyFixtureEvaluator()
            : base(NullLogger<CircularDependencyFixtureEvaluator>.Instance)
        {
        }

        public override string Name => "CircularDependencyEvaluator";

        public override string[] SupportedLanguages => ["C#"];

        public override string[] SupportedFrameworks => ["*"];

        protected override Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var context = Context
                ?? throw new InvalidOperationException("The evaluator context was not assigned.");

            IEnumerable<ArchitectureEvaluatorResult> facts =
            [
                new ArchitectureEvaluatorResult(Name, context.ProjectName)
                {
                    Category = "ArchitectureSummary",
                    Metrics =
                    {
                        ["CircularDependencyCount"] = 1
                    },
                    Metadata =
                    {
                        ["EvaluationScope"] = "Project"
                    }
                }
            ];

            return Task.FromResult(facts);
        }
    }

    private sealed class TemporaryProject : IDisposable
    {
        public string Root { get; } = Path.Combine(
            FindRepositoryRoot(),
            $"AegisAnalyzerIntegration_{Guid.NewGuid():N}");

        public TemporaryProject() => Directory.CreateDirectory(Root);

        public string Write(string relativePath, string content)
        {
            var fullPath = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "sources"))
                    && Directory.Exists(Path.Combine(directory.FullName, "tests")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Aegis repository root for analyzer fixtures.");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
