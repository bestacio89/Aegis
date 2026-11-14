using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinProjectDependencyPipelineTests
{
    [Fact]
    public async Task Analyses_production_modules_dependency_first_then_supplies_scoped_outputs_to_tests()
    {
        var workspace = Directory.CreateTempSubdirectory("aegis-kotlin-project-dependencies-");
        try
        {
            var consumer = CreateModule(
                workspace.FullName,
                "a-consumer",
                [
                    ("src/main/kotlin", ProjectSourceRootOwnership.Production, "Consumer.kt"),
                    ("src/test/kotlin", ProjectSourceRootOwnership.Test, "ConsumerTest.kt")
                ]);
            var dependency = CreateModule(
                workspace.FullName,
                "m-production-dependency",
                [
                    ("src/main/kotlin", ProjectSourceRootOwnership.Production, "Dependency.kt")
                ]);
            var testSupport = CreateModule(
                workspace.FullName,
                "z-test-support",
                [
                    ("src/main/kotlin", ProjectSourceRootOwnership.Production, "TestSupport.kt")
                ],
                isTestSupport: true);

            var context = new ProjectArchitectureContext
            {
                ProjectName = "kotlin-multi-module",
                RootPath = workspace.FullName,
                Language = "Kotlin",
                Modules = [consumer, dependency, testSupport],
                Dependencies =
                [
                    ProjectDependency(consumer, dependency, "Production"),
                    ProjectDependency(consumer, testSupport, "Test")
                ]
            };
            var adapter = new RecordingKotlinExecutionAdapter();
            var orchestrator = new KotlinSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyJvmClasspathResolver(),
                NullLogger<KotlinSemanticAnalysisOrchestrator>.Instance);
            await using var executionContext = new SemanticAnalysisExecutionContext(
                NullLogger<SemanticAnalysisExecutionContext>.Instance);

            await orchestrator.AnalyzeAsync(context, executionContext);

            Assert.Equal(
                [
                    ("m-production-dependency", false),
                    ("a-consumer", false),
                    ("z-test-support", true),
                    ("a-consumer", true)
                ],
                adapter.Requests.Select(request =>
                    (Path.GetFileName(request.ProjectPath), request.IncludeTestCode)));

            var dependencyOutput = adapter.Outputs[(Path.GetFullPath(dependency.Path), false)];
            var consumerProductionOutput = adapter.Outputs[(Path.GetFullPath(consumer.Path), false)];
            var testSupportOutput = adapter.Outputs[(Path.GetFullPath(testSupport.Path), true)];
            var consumerProductionRequest = Assert.Single(adapter.Requests, request =>
                request.ProjectPath == Path.GetFullPath(consumer.Path) && !request.IncludeTestCode);
            var consumerTestRequest = Assert.Single(adapter.Requests, request =>
                request.ProjectPath == Path.GetFullPath(consumer.Path) && request.IncludeTestCode);

            Assert.Contains(dependencyOutput, consumerProductionRequest.ClasspathEntries);
            Assert.DoesNotContain(testSupportOutput, consumerProductionRequest.ClasspathEntries);
            Assert.Contains(dependencyOutput, consumerTestRequest.ClasspathEntries);
            Assert.Contains(consumerProductionOutput, consumerTestRequest.ClasspathEntries);
            Assert.Contains(testSupportOutput, consumerTestRequest.ClasspathEntries);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    private static ProjectModuleContext CreateModule(
        string workspace,
        string name,
        IReadOnlyCollection<(string Directory, ProjectSourceRootOwnership Ownership, string FileName)> roots,
        bool isTestSupport = false)
    {
        var modulePath = Path.Combine(workspace, name);
        var buildFile = Path.Combine(modulePath, "build.gradle.kts");
        var sourceRoots = new List<ProjectSourceRoot>();

        foreach (var (relativeDirectory, ownership, fileName) in roots)
        {
            var root = Path.Combine(modulePath, relativeDirectory);
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, fileName),
                $"package fixture.{name.Replace('-', '_')}\nclass {Path.GetFileNameWithoutExtension(fileName)}");
            sourceRoots.Add(new ProjectSourceRoot
            {
                Directory = root,
                Ownership = ownership,
                Origin = ProjectSourceRootOrigin.Conventional,
                Languages = ["Kotlin"]
            });
        }

        return new ProjectModuleContext
        {
            Name = name,
            Path = modulePath,
            ProjectFile = buildFile,
            ProjectFiles = [buildFile],
            BuildSystem = ArchitectureBuildSystem.Gradle,
            Languages = ["Kotlin"],
            SourceRoots = sourceRoots,
            GradleBuildModel = new GradleBuildModel
            {
                BuildFile = buildFile,
                Dialect = "Kotlin",
                Plugins = [new GradleBuildModel.Plugin("org.jetbrains.kotlin.jvm", "2.4.21", true)],
                KotlinPluginVersion = "2.4.21",
                KotlinLanguageVersion = "2.4",
                KotlinApiVersion = "2.4",
                KotlinJvmTarget = "21",
                KotlinCompilerConfigurationInferred = false
            },
            IsTestSupport = isTestSupport
        };
    }

    private static ArchitectureDependencyContext ProjectDependency(
        ProjectModuleContext source,
        ProjectModuleContext target,
        string scope) =>
        new()
        {
            Source = source.Name,
            SourceProjectPath = source.Path,
            Target = target.Name,
            TargetProjectPath = target.Path,
            DependencyType = "GradleProjectDependency",
            Scope = scope
        };

    private sealed class EmptyJvmClasspathResolver : IJvmClasspathResolver
    {
        public JvmClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode) =>
            new([], [], HasDeclaredDependencies: false, IsComplete: true);
    }

    private sealed class RecordingKotlinExecutionAdapter : ISemanticAnalysisExecutionAdapter
    {
        public string AdapterId => "kotlin-project-dependency-test";

        public string Language => "Kotlin";

        public IReadOnlyCollection<string> SupportedCapabilities =>
            ["symbols", "relationships", "compiler-diagnostics", "jvm-interop"];

        public List<SemanticAnalysisRequest> Requests { get; } = [];

        public Dictionary<(string ProjectPath, bool IncludeTestCode), string> Outputs { get; } = [];

        public bool CanAnalyze(SemanticAnalysisRequest request) =>
            request.Language.Equals(Language, StringComparison.OrdinalIgnoreCase);

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CreateRun(request));
        }

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            SemanticAnalysisExecutionContext executionContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            var artifactRoot = executionContext.CreateArtifactRoot();
            var outputDirectory = Path.Combine(artifactRoot, "classes");
            Directory.CreateDirectory(outputDirectory);
            executionContext.RegisterCompilationOutput(
                request.ProjectPath,
                request.IncludeTestCode,
                artifactRoot,
                outputDirectory);
            Outputs[(Path.GetFullPath(request.ProjectPath), request.IncludeTestCode)] = outputDirectory;

            return ValueTask.FromResult(CreateRun(request));
        }

        private SemanticAnalysisRun CreateRun(SemanticAnalysisRequest request)
        {
            var now = DateTimeOffset.UtcNow;
            return new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = Language,
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = request.SourceFiles.Count,
                StartedAtUtc = now,
                CompletedAtUtc = now
            };
        }
    }
}
