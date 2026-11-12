using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JavaSemanticAnalysisOrchestratorTests
{
    [Fact]
    public async Task Analyses_only_the_source_roots_owned_by_each_java_module()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var firstRoot = Path.Combine(repository, "module-a", "src", "main", "java");
        var secondRoot = Path.Combine(repository, "module-b", "src", "main", "java");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var firstFile = Path.Combine(firstRoot, "A.java");
        var secondFile = Path.Combine(secondRoot, "B.java");
        await File.WriteAllTextAsync(firstFile, "package sample; public class A {}");
        await File.WriteAllTextAsync(secondFile, "package sample; public class B {}");

        try
        {
            var adapter = new RecordingJavaAdapter();
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);
            var context = new ProjectArchitectureContext
            {
                ProjectName = "multi-module",
                RootPath = repository,
                Language = "Java",
                Modules =
                [
                    new ProjectModuleContext
                    {
                        Name = "aggregator",
                        Path = repository,
                        BuildSystem = ArchitectureBuildSystem.Maven,
                        Languages = ["Java"]
                    },
                    CreateModule("module-a", Path.Combine(repository, "module-a"), firstRoot),
                    CreateModule("module-b", Path.Combine(repository, "module-b"), secondRoot)
                ]
            };

            await orchestrator.AnalyzeAsync(context);

            Assert.Equal(2, context.SemanticAnalysisRuns.Count);
            Assert.Equal(2, adapter.Requests.Count);
            Assert.Equal(new[] { firstFile }, adapter.Requests[0].SourceFiles);
            Assert.Equal(new[] { secondFile }, adapter.Requests[1].SourceFiles);
            Assert.DoesNotContain(adapter.Requests, request =>
                request.ProjectPath.Equals(repository, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private static ProjectModuleContext CreateModule(string name, string path, string sourceRoot) =>
        new()
        {
            Name = name,
            Path = path,
            BuildSystem = ArchitectureBuildSystem.Maven,
            Languages = ["Java"],
            SourceRoots =
            [
                new ProjectSourceRoot
                {
                    Directory = sourceRoot,
                    Ownership = ProjectSourceRootOwnership.Production,
                    Origin = ProjectSourceRootOrigin.Explicit,
                    ContainsJavaSources = true
                }
            ]
        };

    private sealed class RecordingJavaAdapter : ISemanticAnalysisAdapter
    {
        public List<SemanticAnalysisRequest> Requests { get; } = [];

        public string AdapterId => "fixture-java";
        public string Language => "Java";
        public IReadOnlyCollection<string> SupportedCapabilities => ["symbols"];

        public bool CanAnalyze(SemanticAnalysisRequest request) =>
            request.Language.Equals("Java", StringComparison.OrdinalIgnoreCase);

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = "Java",
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = request.SourceFiles.Count
            });
        }
    }

    private sealed class EmptyClasspathResolver : IJavaClasspathResolver
    {
        public JavaClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode) =>
            new(Array.Empty<string>(), Array.Empty<string>(), false, true);
    }
}
