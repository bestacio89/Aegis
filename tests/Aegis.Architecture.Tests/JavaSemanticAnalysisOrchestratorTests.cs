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

    [Fact]
    public async Task Adds_compiler_confirmed_framework_annotation_evidence()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(repository, "src", "main", "java");
        Directory.CreateDirectory(sourceRoot);
        var sourceFile = Path.Combine(sourceRoot, "Service.java");
        await File.WriteAllTextAsync(sourceFile, "package sample; public class Service {}");

        try
        {
            var adapter = new AnnotationJavaAdapter(sourceFile);
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);
            var module = new ProjectModuleContext
            {
                Name = "service",
                Path = repository,
                Languages = ["Java"],
                Framework = "Java SE",
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
            var context = new ProjectArchitectureContext
            {
                ProjectName = "service",
                RootPath = repository,
                Language = "Java",
                Framework = "Java SE",
                Modules = [module]
            };

            await orchestrator.AnalyzeAsync(context);

            Assert.Contains("Spring Framework", module.Frameworks);
            Assert.Contains("Spring Framework", context.DetectedFrameworks);
            var evidence = Assert.Single(module.FrameworkEvidence);
            Assert.Equal(FrameworkEvidenceKind.Web, evidence.Kind);
            Assert.False(evidence.IsTestOnly);
            Assert.Equal(SemanticEvidenceSource.Compiler, Assert.Single(evidence.Provenance).Source);
            Assert.Equal(sourceFile, evidence.Provenance[0].FilePath);
            Assert.Equal("Spring Framework", context.Framework);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Adds_test_framework_evidence_from_declared_maven_test_dependencies()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(repository, "src", "test", "java");
        Directory.CreateDirectory(sourceRoot);
        var sourceFile = Path.Combine(sourceRoot, "ServiceTests.java");
        var pomFile = Path.Combine(repository, "pom.xml");
        await File.WriteAllTextAsync(sourceFile, "package sample; public class ServiceTests {}");
        await File.WriteAllTextAsync(pomFile, "<project />");

        try
        {
            var adapter = new AnnotationJavaAdapter(sourceFile, "sample.CustomAnnotation", isTestCode: true);
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);
            var module = new ProjectModuleContext
            {
                Name = "service-tests",
                Path = repository,
                Languages = ["Java"],
                IsTestProject = true,
                MavenModel = new MavenProjectModel
                {
                    ProjectFile = pomFile,
                    ProjectCoordinates = new MavenProjectModel.Coordinates("sample", "service-tests", "1.0.0"),
                    Dependencies =
                    [
                        new MavenProjectModel.Dependency(
                            "org.junit.jupiter",
                            "junit-jupiter",
                            "5.10.2",
                            "test",
                            "jar",
                            null,
                            false,
                            false,
                            false,
                            [],
                            pomFile,
                            false)
                    ]
                },
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = sourceRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    }
                ]
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "service-tests",
                RootPath = repository,
                Language = "Java",
                Framework = "Java SE",
                Modules = [module]
            };

            await orchestrator.AnalyzeAsync(context);

            var evidence = Assert.Single(module.FrameworkEvidence);
            Assert.Equal("JUnit 5", evidence.Name);
            Assert.Equal("5.10.2", evidence.Version);
            Assert.Equal(FrameworkEvidenceKind.Testing, evidence.Kind);
            Assert.True(evidence.IsTestOnly);
            Assert.Equal(SemanticEvidenceSource.BuildModel, Assert.Single(evidence.Provenance).Source);
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

    private sealed class AnnotationJavaAdapter : ISemanticAnalysisAdapter
    {
        private readonly string _sourceFile;
        private readonly string _annotationName;
        private readonly bool _isTestCode;

        public AnnotationJavaAdapter(
            string sourceFile,
            string annotationName = "org.springframework.web.bind.annotation.RestController",
            bool isTestCode = false)
        {
            _sourceFile = sourceFile;
            _annotationName = annotationName;
            _isTestCode = isTestCode;
        }

        public string AdapterId => "eclipse-jdt";
        public string Language => "Java";
        public IReadOnlyCollection<string> SupportedCapabilities => ["symbols", "annotations"];

        public bool CanAnalyze(SemanticAnalysisRequest request) => true;

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            var run = new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = "Java",
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = 1,
                Symbols =
                [
                    new SemanticSymbol { Id = "source", QualifiedName = "sample.Service" },
                    new SemanticSymbol { Id = "annotation", QualifiedName = _annotationName }
                ],
                Relationships =
                [
                    new SemanticRelationship
                    {
                        Id = "annotation-use",
                        SourceSymbolId = "source",
                        TargetSymbolId = "annotation",
                        Kind = SemanticRelationshipKind.UsesAnnotation,
                        ResolutionStatus = SemanticResolutionStatus.Resolved,
                        IsTestCode = _isTestCode,
                        Provenance =
                        [
                            new SemanticEvidenceProvenance
                            {
                                Source = SemanticEvidenceSource.Compiler,
                                FilePath = _sourceFile,
                                StartLine = 4,
                                StartColumn = 1,
                                Confidence = 1.0
                            }
                        ]
                    }
                ]
            };
            return ValueTask.FromResult(run);
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
