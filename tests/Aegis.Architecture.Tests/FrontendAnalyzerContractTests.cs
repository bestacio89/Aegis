using Aegis.Architecture.Evaluators.FrontEnd.src;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aegis.Architecture.Tests;

[CollectionDefinition("Frontend analyzer environment", DisableParallelization = true)]
public sealed class FrontendAnalyzerEnvironmentCollection { }

[Collection("Frontend analyzer environment")]
public sealed class FrontendAnalyzerContractTests
{
    [Fact]
    public async Task AngularEvaluator_maps_verified_analyzer_output_without_claiming_semantic_analysis()
    {
        using var project = new TemporaryProject();
        project.Write("front/src/app.component.ts", "export class AppComponent {}");
        var analyzerPath = project.Write(
            "frontend-analyzer.mjs",
            """
            console.log(JSON.stringify({
              status: "completed",
              filesAnalyzed: 1,
              angularVersion: "18.0.0",
              files: [
                {
                  path: "front/src/app.component.ts",
                  components: [
                    {
                      name: "AppComponent",
                      selector: "app-root",
                      standalone: true,
                      cyclomaticComplexity: 2
                    }
                  ],
                  modules: []
                }
              ]
            }));
            """);

        using var environment = new FrontendAnalyzerEnvironment(analyzerPath);
        var evaluator = new AngularEvaluator(
            NullLogger<AngularEvaluator>.Instance,
            Options.Create(new AegisArchitecturePolicy()));

        var results = (await evaluator.EvaluateAsync(
            project.Root,
            CreateContext(project, "TypeScript", "Angular"))).ToArray();

        var component = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "File");

        Assert.Equal(1, component.Metrics["AngularComponentCount"]);
        Assert.Equal(0, component.Metrics["AngularModuleCount"]);
        Assert.Equal(100, component.Metrics["SelectorScore"]);
        Assert.Equal(90, component.Metrics["ComplexityScore"]);
        Assert.Equal("AppComponent", component.Metadata["ComponentNames"]);
        Assert.Equal("1", component.Metadata["StandaloneComponentCount"]);
        Assert.Equal("False", component.Metadata["SemanticAnalysisPerformed"]);
        Assert.Contains(results, result =>
            result.Metadata.GetValueOrDefault("EvaluationScope") == "Project"
            && result.Metrics.GetValueOrDefault("AngularComponentCount") == 1);
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task ReactEvaluator_maps_file_metrics_and_analyzer_metadata()
    {
        using var project = new TemporaryProject();
        project.Write("front/src/App.tsx", """
            export function App() {
                return <main>Ready</main>;
            }
            """);
        var analyzerPath = project.Write(
            "frontend-analyzer.mjs",
            """
            console.log(JSON.stringify({
              status: "completed",
              framework: "React",
              analyzerVersion: "test-stub",
              parser: "typescript",
              compilerBacked: false,
              filesAnalyzed: 1,
              files: [
                {
                  filePath: "front/src/App.tsx",
                  sourceLineCount: 4,
                  components: ["App"],
                  componentTyping: [{ name: "App", propsTyped: true, cyclomaticComplexity: 1 }],
                  hooks: [{ name: "useState", line: 2, conditionallyCalled: false }],
                  jsxElementCount: 3,
                  conditionalCount: 1,
                  syntaxDiagnostics: []
                }
              ]
            }));
            """);

        using var environment = new FrontendAnalyzerEnvironment(analyzerPath);
        var evaluator = new ReactEvaluator(
            NullLogger<ReactEvaluator>.Instance,
            Options.Create(new AegisArchitecturePolicy()));

        var results = (await evaluator.EvaluateAsync(
            project.Root,
            CreateContext(project, "TypeScript", "React"))).ToArray();

        var component = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "File");

        Assert.Equal(1, component.Metrics["ComponentCount"]);
        Assert.Equal(1, component.Metrics["HookCount"]);
        Assert.Equal(100, component.Metrics["HookDisciplineScore"]);
        Assert.Equal(100, component.Metrics["TypingScore"]);
        Assert.Equal(100, component.Metrics["ComplexityScore"]);
        Assert.Equal(3, component.Metrics["JsxElementCount"]);
        Assert.Equal(1, component.Metrics["ConditionalCount"]);
        Assert.Equal(0, component.Metrics["SyntaxDiagnosticCount"]);
        Assert.Equal("test-stub", component.Metadata["AnalyzerVersion"]);
        Assert.Equal("False", component.Metadata["CompilerBacked"]);
        Assert.Contains(results, result =>
            result.Metadata.GetValueOrDefault("EvaluationScope") == "Project"
            && result.Metrics.GetValueOrDefault("FilesMapped") == 1);
        AssertAllMetricsAreFinite(results);
    }

    private static ProjectArchitectureContext CreateContext(
        TemporaryProject project,
        string language,
        string? framework) =>
        new()
        {
            ProjectName = "FrontendAnalyzerFixture",
            RootPath = project.Root,
            Language = language,
            Framework = framework,
            Confidence = 0.95
        };

    private static void AssertAllMetricsAreFinite(
        IEnumerable<ArchitectureEvaluatorResult> results)
    {
        foreach (var result in results)
        {
            Assert.All(result.Metrics, metric =>
                Assert.True(
                    double.IsFinite(metric.Value),
                    $"Metric '{metric.Key}' was not finite: {metric.Value}."));
        }
    }

    private sealed class FrontendAnalyzerEnvironment : IDisposable
    {
        private readonly string? _previousAnalyzerPath =
            Environment.GetEnvironmentVariable("AEGIS_FRONTEND_ANALYZER_PATH");
        private readonly string? _previousNodeExecutable =
            Environment.GetEnvironmentVariable("AEGIS_NODE_EXECUTABLE");
        private readonly string? _previousTimeout =
            Environment.GetEnvironmentVariable("AEGIS_FRONTEND_ANALYZER_TIMEOUT_SECONDS");

        public FrontendAnalyzerEnvironment(string analyzerPath)
        {
            Environment.SetEnvironmentVariable(
                "AEGIS_FRONTEND_ANALYZER_PATH",
                analyzerPath);
            Environment.SetEnvironmentVariable("AEGIS_NODE_EXECUTABLE", "node");
            Environment.SetEnvironmentVariable(
                "AEGIS_FRONTEND_ANALYZER_TIMEOUT_SECONDS",
                "30");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(
                "AEGIS_FRONTEND_ANALYZER_PATH",
                _previousAnalyzerPath);
            Environment.SetEnvironmentVariable(
                "AEGIS_NODE_EXECUTABLE",
                _previousNodeExecutable);
            Environment.SetEnvironmentVariable(
                "AEGIS_FRONTEND_ANALYZER_TIMEOUT_SECONDS",
                _previousTimeout);
        }
    }

    private sealed class TemporaryProject : IDisposable
    {
        public string Root { get; } = Path.Combine(
            FindRepositoryRoot(),
            $"AegisFrontendAnalyzer_{Guid.NewGuid():N}");

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
                "Could not locate the Aegis repository root for frontend fixtures.");
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
