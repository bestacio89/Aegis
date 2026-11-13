using Aegis.Architecture.Aggregation;
using Aegis.Architecture.Analysis;
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
            "CircularDependencyEvaluator",
            (_, _) => Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            [
                new ArchitectureEvaluatorResult("CircularDependencyEvaluator", "SampleProject")
                {
                    Category = "Architecture",
                    Evidence = "Dependency cycle: Orders -> Payments -> Orders.",
                    Metrics = new Dictionary<string, double>
                    {
                        ["CircularDependencyCount"] = 1
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["EvaluationScope"] = "Project",
                        ["Evidence.CircularDependencyCount"] =
                            "Dependency cycle: Orders -> Payments -> Orders."
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

        Assert.Contains(report.Facts, fact => fact.Source == "CircularDependencyEvaluator");
        Assert.Contains(report.Diagnostics, diagnostic =>
            diagnostic.Code == "EvaluatorFailed"
            && diagnostic.Component == "FailingEvaluator"
            && diagnostic.AffectsCoverage);
        Assert.Contains(report.Results, result => result.RuleId == "AEG-ARCH-R1");
        Assert.Equal(1, report.ChecksPerformed);
    }


    [Fact]
    public async Task RunAsync_preserves_semantic_diagnostics_with_run_provenance()
    {
        using var directory = new TemporaryDirectory();
        var runId = Guid.NewGuid().ToString("N");
        var sourcePath = Path.Combine(directory.Path, "src", "main", "java", "Example.java");
        var report = await CreateEngine([]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "JavaProject",
                RootPath = directory.Path,
                Language = "Java",
                SemanticAnalysisRuns =
                [
                    new SemanticAnalysisRun
                    {
                        RunId = runId,
                        Language = "Java",
                        EngineId = "eclipse-jdt",
                        ProjectPath = directory.Path,
                        Status = Aegis.Shared.Architecture.Enums.SemanticAnalysisStatus.PartiallyCompleted,
                        Diagnostics =
                        [
                            new AnalysisDiagnostic
                            {
                                Code = "UnresolvedType",
                                Component = "JdtSemanticAnalysisEngine",
                                Message = "The type 'ExternalType' could not be resolved.",
                                Target = sourcePath,
                                Severity = "Warning",
                                AffectsCoverage = true
                            }
                        ]
                    }
                ]
            });

        var diagnostic = Assert.Single(report.Diagnostics, item => item.Code == "UnresolvedType");
        Assert.Equal("SemanticAnalysis", diagnostic.Source);
        Assert.Equal("Java", diagnostic.Language);
        Assert.Equal("eclipse-jdt", diagnostic.EngineId);
        Assert.Equal(runId, diagnostic.RunId);
        Assert.Equal(directory.Path, diagnostic.ProjectPath);
        Assert.Equal(sourcePath, diagnostic.Target);
        Assert.Equal("Warning", diagnostic.Severity);
        Assert.True(diagnostic.AffectsCoverage);
        Assert.False(report.Metrics.AssessmentComplete);
    }

    [Fact]
    public async Task RunAsync_marks_partial_semantic_runs_without_diagnostic_details()
    {
        using var directory = new TemporaryDirectory();
        var report = await CreateEngine([]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "JavaProject",
                RootPath = directory.Path,
                Language = "Java",
                SemanticAnalysisRuns =
                [
                    new SemanticAnalysisRun
                    {
                        RunId = "partial-run",
                        Language = "Java",
                        EngineId = "eclipse-jdt",
                        ProjectPath = directory.Path,
                        Status = Aegis.Shared.Architecture.Enums.SemanticAnalysisStatus.PartiallyCompleted
                    }
                ]
            });

        var diagnostic = Assert.Single(report.Diagnostics, item =>
            item.Code == "SemanticAnalysisIncomplete");
        Assert.Equal("partial-run", diagnostic.RunId);
        Assert.Equal("eclipse-jdt", diagnostic.EngineId);
        Assert.True(diagnostic.AffectsCoverage);
        Assert.False(report.Metrics.AssessmentComplete);
    }

    [Fact]
    public async Task RunAsync_preserves_diagnostics_reported_by_completed_evaluators()
    {
        using var directory = new TemporaryDirectory();
        var evaluator = new FixtureEvaluator(
            "ConfigurationEvaluator",
            (_, _) => Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            [
                new ArchitectureEvaluatorResult("ConfigurationEvaluator", directory.Path)
                {
                    Category = "InfrastructureSummary",
                    Metrics = new Dictionary<string, double>
                    {
                        ["ConfigurationFileCount"] = 4,
                        ["DiagnosticCount"] = 2
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["EvaluationScope"] = "Project",
                        ["AnalysisStatus"] = "CompletedWithDiagnostics",
                        ["Diagnostics"] = string.Join(
                            Environment.NewLine,
                            "Configuration syntax validation failed for 'appsettings.json'.",
                            "Access denied for 'secrets.yaml'.")
                    }
                }
            ]));

        var report = await CreateEngine([evaluator]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "SampleProject",
                RootPath = directory.Path,
                Language = "C#"
            });

        Assert.Equal(2, report.Diagnostics.Count);
        Assert.All(report.Diagnostics, diagnostic =>
        {
            Assert.Equal("EvaluatorReportedDiagnostic", diagnostic.Code);
            Assert.Equal("ConfigurationEvaluator", diagnostic.Component);
            Assert.True(diagnostic.AffectsCoverage);
        });
        Assert.Contains(report.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("appsettings.json", StringComparison.Ordinal));
        Assert.Contains(report.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("secrets.yaml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_preserves_reported_diagnostic_count_when_details_are_truncated()
    {
        using var directory = new TemporaryDirectory();
        var evaluator = new FixtureEvaluator(
            "ConfigurationEvaluator",
            (_, _) => Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            [
                new ArchitectureEvaluatorResult("ConfigurationEvaluator", directory.Path)
                {
                    Category = "InfrastructureSummary",
                    Metrics = new Dictionary<string, double>
                    {
                        ["DiagnosticCount"] = 3
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["EvaluationScope"] = "Project",
                        ["AnalysisStatus"] = "CompletedWithDiagnostics",
                        ["Diagnostics"] = "Invalid JSON input.\nUnreadable YAML input."
                    }
                }
            ]));

        var report = await CreateEngine([evaluator]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "SampleProject",
                RootPath = directory.Path,
                Language = "C#"
            });

        Assert.Equal(3, report.Diagnostics.Count);
        Assert.Equal(2, report.Diagnostics.Count(diagnostic =>
            diagnostic.Code == "EvaluatorReportedDiagnostic"));
        var truncated = Assert.Single(report.Diagnostics, diagnostic =>
            diagnostic.Code == "EvaluatorDiagnosticDetailsTruncated");
        Assert.Contains("reported 3 diagnostics", truncated.Message);
        Assert.True(truncated.AffectsCoverage);
    }

    [Fact]
    public async Task RunAsync_executes_framework_specific_evaluators_only_for_matching_project_scopes()
    {
        using var directory = new TemporaryDirectory();
        var backendPath = Path.Combine(directory.Path, "Backend");
        var blazorPath = Path.Combine(directory.Path, "Blazor");
        Directory.CreateDirectory(backendPath);
        Directory.CreateDirectory(blazorPath);

        File.WriteAllText(Path.Combine(backendPath, "Backend.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(backendPath, "Program.cs"), "public class Program { }");
        File.WriteAllText(Path.Combine(blazorPath, "Blazor.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(blazorPath, "Program.cs"), "public class Program { }");
        File.WriteAllText(Path.Combine(blazorPath, "Home.razor"), "@page \"/\"\n<h1>Home</h1>");

        var context = Aegis.Architecture.Analysis.ProjectArchitectureContextDetector.Detect(directory.Path);
        var evaluatedPaths = new List<string>();
        var evaluator = new FixtureEvaluator(
            "BlazorOnlyEvaluator",
            (path, _) =>
            {
                evaluatedPaths.Add(path);
                return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>([]);
            },
            supportedLanguages: ["C#"],
            supportedFrameworks: ["Blazor"]);

        await CreateEngine([evaluator]).RunAsync(directory.Path, context);

        Assert.Contains(evaluatedPaths, path =>
            string.Equals(path, blazorPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(evaluatedPaths, path =>
            string.Equals(path, backendPath, StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task RunAsync_executes_graph_evaluators_once_at_repository_scope()
    {
        using var directory = new TemporaryDirectory();
        var backendPath = Path.Combine(directory.Path, "Backend");
        var frontendPath = Path.Combine(directory.Path, "Frontend");
        Directory.CreateDirectory(backendPath);
        Directory.CreateDirectory(frontendPath);

        File.WriteAllText(Path.Combine(backendPath, "Backend.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(backendPath, "Program.cs"), "public class Program { }");
        File.WriteAllText(Path.Combine(frontendPath, "Frontend.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(frontendPath, "Program.cs"), "public class FrontendProgram { }");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);
        var evaluatedPaths = new List<string>();
        var evaluator = new FixtureEvaluator(
            "CircularDependencyEvaluator",
            (path, _) =>
            {
                evaluatedPaths.Add(path);
                return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>([]);
            },
            supportedLanguages: ["C#"],
            supportedFrameworks: ["ASP.NET Core"]);

        await CreateEngine([evaluator]).RunAsync(directory.Path, context);

        Assert.Equal(new[] { directory.Path }, evaluatedPaths);
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

    [Fact]
    public async Task RunAsync_skips_evaluator_when_required_semantic_capabilities_are_missing()
    {
        using var directory = new TemporaryDirectory();
        var invoked = false;
        var evaluator = new FixtureEvaluator(
            "SemanticEvaluator",
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>([]);
            },
            supportedLanguages: ["C#"],
            requiredSemanticCapabilities: ["relationships"]);

        var report = await CreateEngine([evaluator]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "SemanticProject",
                RootPath = directory.Path,
                Language = "C#",
                SemanticAnalysisRuns =
                [
                    new SemanticAnalysisRun
                    {
                        Language = "C#",
                        ProjectPath = directory.Path,
                        Status = Aegis.Shared.Architecture.Enums.SemanticAnalysisStatus.Completed,
                        Capabilities = ["symbols"]
                    }
                ]
            });

        Assert.False(invoked);
        var diagnostic = Assert.Single(report.Diagnostics, item =>
            item.Code == "EvaluatorSemanticCapabilitiesUnavailable");
        Assert.Equal("SemanticEvaluator", diagnostic.Component);
        Assert.Contains("relationships", diagnostic.Message);
        Assert.True(diagnostic.AffectsCoverage);
    }

    [Fact]
    public async Task RunAsync_executes_evaluator_when_all_required_semantic_capabilities_are_available()
    {
        using var directory = new TemporaryDirectory();
        var invoked = false;
        var evaluator = new FixtureEvaluator(
            "SemanticEvaluator",
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>([]);
            },
            supportedLanguages: ["C#"],
            requiredSemanticCapabilities: ["symbols", "relationships"]);

        var report = await CreateEngine([evaluator]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "SemanticProject",
                RootPath = directory.Path,
                Language = "C#",
                SemanticAnalysisRuns =
                [
                    new SemanticAnalysisRun
                    {
                        Language = "C#",
                        ProjectPath = directory.Path,
                        Status = Aegis.Shared.Architecture.Enums.SemanticAnalysisStatus.Completed,
                        Capabilities = ["symbols", "relationships"]
                    }
                ]
            });

        Assert.True(invoked);
        Assert.DoesNotContain(report.Diagnostics, item =>
            item.Code == "EvaluatorSemanticCapabilitiesUnavailable");
    }

    [Fact]
    public async Task RunAsync_matches_evaluator_language_against_all_languages_owned_by_a_module()
    {
        using var directory = new TemporaryDirectory();
        var invoked = false;
        var evaluator = new FixtureEvaluator(
            "JavaOnlyEvaluator",
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>([]);
            },
            supportedLanguages: ["Java"]);

        await CreateEngine([evaluator]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "MixedProject",
                RootPath = directory.Path,
                Language = "C#",
                Modules =
                [
                    new ProjectModuleContext
                    {
                        Name = "MixedProject",
                        Path = directory.Path,
                        Languages = ["C#", "Java"]
                    }
                ]
            });

        Assert.True(invoked);
    }

    [Fact]
    public async Task RunAsync_matches_framework_support_against_all_frameworks_owned_by_a_module()
    {
        using var directory = new TemporaryDirectory();
        var invoked = false;
        var evaluator = new FixtureEvaluator(
            "SpringEvaluator",
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>([]);
            },
            supportedLanguages: ["Java"],
            supportedFrameworks: ["Spring Boot"]);

        await CreateEngine([evaluator]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "MixedJvmProject",
                RootPath = directory.Path,
                Language = "C#",
                Framework = "C#",
                Modules =
                [
                    new ProjectModuleContext
                    {
                        Name = "MixedJvmProject",
                        Path = directory.Path,
                        Languages = ["C#", "Java"],
                        Frameworks = ["Java", "Spring Boot"]
                    }
                ]
            });

        Assert.True(invoked);
    }

    [Fact]
    public async Task RunAsync_fails_closed_when_required_semantic_capabilities_have_no_detected_language()
    {
        using var directory = new TemporaryDirectory();
        var invoked = false;
        var evaluator = new FixtureEvaluator(
            "SemanticEvaluator",
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>([]);
            },
            requiredSemanticCapabilities: ["symbols"]);

        var report = await CreateEngine([evaluator]).RunAsync(
            directory.Path,
            new ProjectArchitectureContext
            {
                ProjectName = "UnknownProject",
                RootPath = directory.Path,
                Language = "Unknown"
            });

        Assert.False(invoked);
        var diagnostic = Assert.Single(report.Diagnostics, item =>
            item.Code == "EvaluatorSemanticCapabilitiesUnavailable");
        Assert.Contains("Unknown", diagnostic.Message);
        Assert.True(diagnostic.AffectsCoverage);
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
                Task<IEnumerable<ArchitectureEvaluatorResult>>> evaluate,
            string[]? supportedLanguages = null,
            string[]? supportedFrameworks = null,
            string[]? requiredSemanticCapabilities = null)
        {
            Name = name;
            _evaluate = evaluate;
            SupportedLanguages = supportedLanguages ?? ["*"];
            SupportedFrameworks = supportedFrameworks ?? ["*"];
            RequiredSemanticCapabilities = requiredSemanticCapabilities ?? [];
        }

        public string Name { get; }
        public string[] SupportedLanguages { get; }
        public string[] SupportedFrameworks { get; }
        public IReadOnlyCollection<string> RequiredSemanticCapabilities { get; }
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
