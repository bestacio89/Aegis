using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinSemanticAnalysisOrchestratorTests
{
    [Fact]
    public async Task Separates_kotlin_production_and_test_roots_and_supplies_matching_java_sources()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var modulePath = Path.Combine(repository, "service");
        var kotlinMainRoot = Path.Combine(modulePath, "src", "main", "kotlin");
        var kotlinTestRoot = Path.Combine(modulePath, "src", "test", "kotlin");
        var javaMainRoot = Path.Combine(modulePath, "src", "main", "java");
        var javaTestRoot = Path.Combine(modulePath, "src", "test", "java");
        Directory.CreateDirectory(kotlinMainRoot);
        Directory.CreateDirectory(kotlinTestRoot);
        Directory.CreateDirectory(javaMainRoot);
        Directory.CreateDirectory(javaTestRoot);

        var kotlinMainFile = Path.Combine(kotlinMainRoot, "sample", "Service.kt");
        var kotlinTestFile = Path.Combine(kotlinTestRoot, "sample", "ServiceTests.kt");
        Directory.CreateDirectory(Path.GetDirectoryName(kotlinMainFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(kotlinTestFile)!);
        await File.WriteAllTextAsync(kotlinMainFile, "package sample\nclass Service");
        await File.WriteAllTextAsync(kotlinTestFile, "package sample\nclass ServiceTests");
        await File.WriteAllTextAsync(Path.Combine(javaMainRoot, "JavaService.java"), "class JavaService {}");
        await File.WriteAllTextAsync(Path.Combine(javaTestRoot, "JavaServiceTests.java"), "class JavaServiceTests {}");

        try
        {
            var module = CreateModule(modulePath, kotlinMainRoot, kotlinTestRoot, javaMainRoot, javaTestRoot);
            var context = new ProjectArchitectureContext
            {
                ProjectName = "service",
                RootPath = repository,
                Language = "Kotlin",
                Modules = [module]
            };
            var adapter = new RecordingKotlinAdapter();
            var orchestrator = new KotlinSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<KotlinSemanticAnalysisOrchestrator>.Instance);
            await using var executionContext = new SemanticAnalysisExecutionContext(
                NullLogger<SemanticAnalysisExecutionContext>.Instance);

            await orchestrator.AnalyzeAsync(context, executionContext);

            Assert.Equal(2, adapter.Requests.Count);
            var production = Assert.Single(adapter.Requests, request => !request.IncludeTestCode);
            var test = Assert.Single(adapter.Requests, request => request.IncludeTestCode);

            Assert.Equal([Path.GetFullPath(kotlinMainFile)], production.SourceFiles);
            Assert.Equal([Path.GetFullPath(kotlinTestFile)], test.SourceFiles);
            Assert.Equal([Path.GetFullPath(kotlinMainRoot)], production.SourceRoots);
            Assert.Equal([Path.GetFullPath(kotlinTestRoot)], test.SourceRoots);
            Assert.Empty(production.TestSourceRoots);
            Assert.Equal([Path.GetFullPath(kotlinTestRoot)], test.TestSourceRoots);
            Assert.Equal([Path.GetFullPath(javaMainRoot)], production.ReferencedSourceRoots);
            Assert.Equal(
                new[] { Path.GetFullPath(javaMainRoot), Path.GetFullPath(javaTestRoot) }.OrderBy(path => path, PathComparer),
                test.ReferencedSourceRoots);
            Assert.Equal("2.4.21", production.ProviderOptions["kotlin.compilerVersion"]);
            Assert.Equal("2.4", production.ProviderOptions["kotlin.languageVersion"]);
            Assert.Equal("2.4", production.ProviderOptions["kotlin.apiVersion"]);
            Assert.Equal("21", production.ProviderOptions["kotlin.jvmTarget"]);
            Assert.Equal("False", production.ProviderOptions["kotlin.configurationInferred"]);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Reports_multiplatform_modules_as_unsupported_instead_of_treating_them_as_jvm()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            var module = new ProjectModuleContext
            {
                Name = "shared",
                Path = repository,
                Languages = ["Kotlin"],
                GradleBuildModel = new GradleBuildModel
                {
                    Plugins = [new GradleBuildModel.Plugin("org.jetbrains.kotlin.multiplatform", "2.4.21", true)]
                }
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "shared",
                RootPath = repository,
                Language = "Kotlin",
                Modules = [module]
            };
            var adapter = new RecordingKotlinAdapter();
            var orchestrator = new KotlinSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<KotlinSemanticAnalysisOrchestrator>.Instance);

            await orchestrator.AnalyzeAsync(context);

            var run = Assert.Single(context.SemanticAnalysisRuns);
            Assert.Equal(SemanticAnalysisStatus.Unsupported, run.Status);
            Assert.Contains(run.Diagnostics, diagnostic =>
                diagnostic.Code == "KotlinTargetPlatformNotSupported"
                && diagnostic.AffectsCoverage);
            Assert.Empty(adapter.Requests);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Theory]
    [InlineData("com.android.application")]
    [InlineData("com.android.library")]
    [InlineData("com.android.test")]
    [InlineData("com.android.dynamic-feature")]
    [InlineData("com.android.asset-pack")]
    public async Task Reports_android_gradle_modules_as_unsupported_instead_of_treating_them_as_jvm(
        string androidPluginId)
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            var module = new ProjectModuleContext
            {
                Name = "android-module",
                Path = repository,
                Languages = ["Kotlin"],
                GradleBuildModel = new GradleBuildModel
                {
                    Plugins = [new GradleBuildModel.Plugin(androidPluginId, "9.0.0", true)]
                }
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "android-module",
                RootPath = repository,
                Language = "Kotlin",
                Modules = [module]
            };
            var adapter = new RecordingKotlinAdapter();
            var orchestrator = new KotlinSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<KotlinSemanticAnalysisOrchestrator>.Instance);

            await orchestrator.AnalyzeAsync(context);

            var run = Assert.Single(context.SemanticAnalysisRuns);
            Assert.Equal(SemanticAnalysisStatus.Unsupported, run.Status);
            Assert.Contains(run.Diagnostics, diagnostic =>
                diagnostic.Code == "KotlinTargetPlatformNotSupported"
                && diagnostic.AffectsCoverage);
            Assert.Empty(adapter.Requests);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Does_not_send_android_dsl_modules_with_unresolved_plugin_identity_to_the_jvm_worker()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            var module = new ProjectModuleContext
            {
                Name = "android-module",
                Path = repository,
                Languages = ["Kotlin"],
                GradleBuildModel = new GradleBuildModel
                {
                    Android = new GradleAndroidBuildModel
                    {
                        ModuleKind = "Unknown",
                        Diagnostics =
                        [
                            new AnalysisDiagnostic
                            {
                                Code = "GradleAndroidPluginNotStaticallyResolved",
                                Component = "GradleBuildModelResolver",
                                Message = "Android plugin identity is unresolved.",
                                Target = repository,
                                AffectsCoverage = true
                            }
                        ],
                        IsComplete = false
                    }
                }
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "android-module",
                RootPath = repository,
                Language = "Kotlin",
                Modules = [module]
            };
            var adapter = new RecordingKotlinAdapter();
            var orchestrator = new KotlinSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<KotlinSemanticAnalysisOrchestrator>.Instance);

            await orchestrator.AnalyzeAsync(context);

            var run = Assert.Single(context.SemanticAnalysisRuns);
            Assert.Equal(SemanticAnalysisStatus.Unsupported, run.Status);
            Assert.Contains(run.Diagnostics, diagnostic =>
                diagnostic.Code == "KotlinTargetPlatformNotSupported"
                && diagnostic.AffectsCoverage);
            Assert.Empty(adapter.Requests);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private static ProjectModuleContext CreateModule(
        string modulePath,
        string kotlinMainRoot,
        string kotlinTestRoot,
        string javaMainRoot,
        string javaTestRoot) =>
        new()
        {
            Name = "service",
            Path = modulePath,
            BuildSystem = ArchitectureBuildSystem.Gradle,
            Languages = ["Kotlin", "Java"],
            GradleBuildModel = new GradleBuildModel
            {
                Plugins = [new GradleBuildModel.Plugin("org.jetbrains.kotlin.jvm", "2.4.21", true)],
                KotlinPluginVersion = "2.4.21",
                KotlinLanguageVersion = "2.4",
                KotlinApiVersion = "2.4",
                KotlinJvmTarget = "21"
            },
            SourceRoots =
            [
                new ProjectSourceRoot
                {
                    Directory = kotlinMainRoot,
                    Ownership = ProjectSourceRootOwnership.Production,
                    Origin = ProjectSourceRootOrigin.Conventional,
                    Languages = ["Kotlin"]
                },
                new ProjectSourceRoot
                {
                    Directory = kotlinTestRoot,
                    Ownership = ProjectSourceRootOwnership.Test,
                    Origin = ProjectSourceRootOrigin.Conventional,
                    Languages = ["Kotlin"]
                },
                new ProjectSourceRoot
                {
                    Directory = javaMainRoot,
                    Ownership = ProjectSourceRootOwnership.Production,
                    Origin = ProjectSourceRootOrigin.Conventional,
                    ContainsJavaSources = true,
                    Languages = ["Java"]
                },
                new ProjectSourceRoot
                {
                    Directory = javaTestRoot,
                    Ownership = ProjectSourceRootOwnership.Test,
                    Origin = ProjectSourceRootOrigin.Conventional,
                    ContainsJavaSources = true,
                    Languages = ["Java"]
                }
            ]
        };

    private sealed class RecordingKotlinAdapter : ISemanticAnalysisAdapter
    {
        public List<SemanticAnalysisRequest> Requests { get; } = [];

        public string AdapterId => "fixture-kotlin";
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
            Requests.Add(request);
            return ValueTask.FromResult(new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = "Kotlin",
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = request.SourceFiles.Count
            });
        }
    }

    private sealed class EmptyClasspathResolver : IJvmClasspathResolver
    {
        public JvmClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode) =>
            new(Array.Empty<string>(), Array.Empty<string>(), false, true);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
