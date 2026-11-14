using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinFrameworkDetectionTests
{
    [Fact]
    public async Task Analyze_combines_module_owned_dependency_evidence_with_compiler_confirmed_usage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisKotlinFramework_{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "src", "main", "kotlin");
        var sourceFile = Path.Combine(sourceRoot, "example", "Application.kt");
        var buildFile = Path.Combine(root, "build.gradle.kts");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);

        try
        {
            await File.WriteAllTextAsync(sourceFile, "package example\nclass Application");
            await File.WriteAllTextAsync(buildFile, "plugins { kotlin(\"jvm\") version \"2.4.21\" }");

            var module = new ProjectModuleContext
            {
                Name = "ktor-service",
                Path = root,
                BuildSystem = ArchitectureBuildSystem.Gradle,
                Languages = ["Kotlin"],
                Framework = "Kotlin/JVM",
                ProjectFiles = [buildFile],
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = sourceRoot,
                        Ownership = ProjectSourceRootOwnership.Production,
                        Origin = ProjectSourceRootOrigin.Conventional,
                        Languages = ["Kotlin"],
                        EvidenceFile = buildFile
                    }
                ],
                GradleBuildModel = new GradleBuildModel
                {
                    BuildFile = buildFile,
                    Plugins = [new GradleBuildModel.Plugin("org.jetbrains.kotlin.jvm", "2.4.21", true)]
                }
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "ktor-service",
                RootPath = root,
                Language = "Kotlin",
                Modules = [module],
                DependencyEvidence =
                [
                    new ArchitectureDependencyEvidence
                    {
                        Id = "gradle:ktor-server-core",
                        Name = "io.ktor:ktor-server-core-jvm",
                        Ecosystem = "Gradle",
                        Kind = DependencyEvidenceKind.Package,
                        DeclaredVersion = "3.0.0",
                        Scope = "implementation",
                        SourceProjectPath = root,
                        IsDirect = true,
                        Provenance =
                        [
                            new SemanticEvidenceProvenance
                            {
                                AnalyzerId = "gradle-static-model",
                                Source = SemanticEvidenceSource.BuildModel,
                                FilePath = buildFile,
                                Confidence = 1.0
                            }
                        ]
                    }
                ]
            };

            var adapter = new KotlinFrameworkEvidenceAdapter(sourceFile, root);
            var orchestrator = new KotlinSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyJvmClasspathResolver(),
                NullLogger<KotlinSemanticAnalysisOrchestrator>.Instance);

            await orchestrator.AnalyzeAsync(context);

            var declared = Assert.Single(module.FrameworkEvidence, evidence =>
                evidence.Name == "Ktor"
                && evidence.Provenance.Any(item => item.Source == SemanticEvidenceSource.BuildModel));
            Assert.Equal("3.0.0", declared.Version);
            Assert.Equal(FrameworkEvidenceKind.Web, declared.Kind);

            var observed = Assert.Single(module.FrameworkEvidence, evidence =>
                evidence.Name == "Ktor"
                && evidence.Provenance.Any(item => item.Source == SemanticEvidenceSource.Compiler));
            Assert.Null(observed.Version);
            Assert.Equal(FrameworkEvidenceKind.Web, observed.Kind);
            Assert.Contains(context.FrameworkEvidence, evidence => evidence.Id == declared.Id);
            Assert.Contains(context.FrameworkEvidence, evidence => evidence.Id == observed.Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Analyze_classifies_Kotlin_test_libraries_as_test_only_build_evidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisKotlinTestFramework_{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "src", "test", "kotlin");
        var sourceFile = Path.Combine(sourceRoot, "example", "ApplicationTests.kt");
        var buildFile = Path.Combine(root, "build.gradle.kts");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);

        try
        {
            await File.WriteAllTextAsync(sourceFile, "package example\nclass ApplicationTests");
            await File.WriteAllTextAsync(buildFile, "plugins { kotlin(\"jvm\") version \"2.4.21\" }");

            var module = new ProjectModuleContext
            {
                Name = "ktor-service-tests",
                Path = root,
                BuildSystem = ArchitectureBuildSystem.Gradle,
                Languages = ["Kotlin"],
                IsTestProject = true,
                ProjectFiles = [buildFile],
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = sourceRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Conventional,
                        Languages = ["Kotlin"],
                        EvidenceFile = buildFile
                    }
                ],
                GradleBuildModel = new GradleBuildModel
                {
                    BuildFile = buildFile,
                    Plugins = [new GradleBuildModel.Plugin("org.jetbrains.kotlin.jvm", "2.4.21", true)],
                    Dependencies =
                    [
                        new GradleBuildModel.Dependency(
                            "testImplementation", "io.kotest", "kotest-runner-junit5", "5.9.1",
                            null, null, false, false, false, "testImplementation(...)"),
                        new GradleBuildModel.Dependency(
                            "testImplementation", "io.mockk", "mockk", "1.13.16",
                            null, null, false, false, false, "testImplementation(...)")
                    ]
                }
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "ktor-service-tests",
                RootPath = root,
                Language = "Kotlin",
                Modules = [module]
            };

            var orchestrator = new KotlinSemanticAnalysisOrchestrator(
                [new KotlinFrameworkEvidenceAdapter(sourceFile, root)],
                new EmptyJvmClasspathResolver(),
                NullLogger<KotlinSemanticAnalysisOrchestrator>.Instance);

            await orchestrator.AnalyzeAsync(context);

            foreach (var framework in new[] { "Kotest", "MockK" })
            {
                var evidence = Assert.Single(module.FrameworkEvidence, item => item.Name == framework);
                Assert.Equal(FrameworkEvidenceKind.Testing, evidence.Kind);
                Assert.True(evidence.IsTestOnly);
                Assert.Equal(SemanticEvidenceSource.BuildModel, Assert.Single(evidence.Provenance).Source);
                Assert.Contains(context.FrameworkEvidence, item => item.Id == evidence.Id);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class KotlinFrameworkEvidenceAdapter(string sourceFile, string projectPath)
        : ISemanticAnalysisAdapter
    {
        public string AdapterId => "kotlin-framework-evidence-test";
        public string Language => "Kotlin";
        public IReadOnlyCollection<string> SupportedCapabilities =>
            ["symbols", "relationships", "compiler-diagnostics", "jvm-interop"];

        public bool CanAnalyze(SemanticAnalysisRequest request) =>
            request.Language.Equals("Kotlin", StringComparison.OrdinalIgnoreCase);

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = new SemanticSymbol
            {
                Id = "kotlin:source:Application",
                Name = "Application",
                QualifiedName = "example.Application",
                Language = "Kotlin",
                ProjectPath = projectPath,
                FilePath = sourceFile
            };
            var target = new SemanticSymbol
            {
                Id = "kotlin:external:ApplicationCall",
                Name = "ApplicationCall",
                QualifiedName = "io.ktor.server.application.ApplicationCall",
                Language = "Kotlin",
                ProjectPath = projectPath,
                Origin = SemanticSymbolOrigin.External
            };
            return ValueTask.FromResult(new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = "Kotlin",
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = request.SourceFiles.Count,
                Symbols = [source, target],
                Relationships =
                [
                    new SemanticRelationship
                    {
                        Id = "kotlin:call:ktor-application-call",
                        SourceSymbolId = source.Id,
                        TargetSymbolId = target.Id,
                        Kind = SemanticRelationshipKind.Calls,
                        ResolutionStatus = SemanticResolutionStatus.Resolved,
                        Provenance =
                        [
                            new SemanticEvidenceProvenance
                            {
                                AnalyzerId = AdapterId,
                                Source = SemanticEvidenceSource.Compiler,
                                FilePath = sourceFile,
                                StartLine = 2,
                                StartColumn = 1,
                                Confidence = 1.0
                            }
                        ]
                    }
                ]
            });
        }
    }

    private sealed class EmptyJvmClasspathResolver : IJvmClasspathResolver
    {
        public JvmClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode) =>
            new(Array.Empty<string>(), Array.Empty<string>(), false, true);
    }
}
