using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class AndroidSemanticAnalysisOrchestratorTests
{
    [Fact]
    public async Task Java_and_kotlin_orchestrators_send_variant_scoped_sources_and_android_platform_classpath()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var modulePath = Path.Combine(root, "app");
        var javaMain = Path.Combine(modulePath, "src", "main", "java");
        var javaTest = Path.Combine(modulePath, "src", "test", "java");
        var kotlinMain = Path.Combine(modulePath, "src", "main", "kotlin");
        var kotlinTest = Path.Combine(modulePath, "src", "test", "kotlin");
        var sdkRoot = Path.Combine(root, "sdk");
        var androidJar = Path.Combine(sdkRoot, "platforms", "android-35", "android.jar");
        foreach (var directory in new[] { javaMain, javaTest, kotlinMain, kotlinTest, Path.GetDirectoryName(androidJar)! })
            Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(javaMain, "Main.java"), "class Main {}");
        File.WriteAllText(Path.Combine(javaTest, "MainTest.java"), "class MainTest {}");
        File.WriteAllText(Path.Combine(kotlinMain, "Main.kt"), "class KotlinMain");
        File.WriteAllText(Path.Combine(kotlinTest, "MainTest.kt"), "class KotlinMainTest");
        File.WriteAllText(androidJar, "android platform fixture");

        try
        {
            var module = new ProjectModuleContext
            {
                Name = "app",
                Path = modulePath,
                BuildSystem = ArchitectureBuildSystem.Gradle,
                Languages = ["Java", "Kotlin"],
                GradleBuildModel = new GradleBuildModel
                {
                    IsComplete = true,
                    KotlinPluginVersion = "2.4.21",
                    KotlinLanguageVersion = "2.4",
                    KotlinApiVersion = "2.4",
                    Plugins =
                    [
                        new GradleBuildModel.Plugin("com.android.application", "9.0.0", true),
                        new GradleBuildModel.Plugin("org.jetbrains.kotlin.android", "2.4.21", true)
                    ],
                    Android = new GradleAndroidBuildModel
                    {
                        PluginId = "com.android.application",
                        ModuleKind = "Application",
                        KotlinIntegration = "BuiltInDefault",
                        CompileSdk = "35",
                        CompileOptions = new GradleAndroidBuildModel.AndroidCompileOptions("JavaVersion.VERSION_17", "JavaVersion.VERSION_17", false),
                        IsComplete = true,
                        Variants = [new GradleAndroidVariant("debug", [], "debug")],
                        SourceSets =
                        [
                            SourceSet("main", ProjectSourceRootOwnership.Production, GradleAndroidTestKind.None,
                                javaMain, kotlinMain),
                            SourceSet("test", ProjectSourceRootOwnership.Test, GradleAndroidTestKind.Unit,
                                javaTest, kotlinTest)
                        ]
                    }
                }
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "app",
                RootPath = root,
                Language = "Java",
                Modules = [module]
            };
            var classpathResolver = new EmptyClasspathResolver();
            var planner = new AndroidSemanticAnalysisPlanner(
                classpathResolver,
                new AndroidSdkResolver(sdkRoot, new Dictionary<string, string>()));

            var javaAdapter = new RecordingAdapter("Java");
            var javaOrchestrator = new JavaSemanticAnalysisOrchestrator(
                [javaAdapter],
                classpathResolver,
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance,
                androidPlanner: planner);
            await javaOrchestrator.AnalyzeAsync(context);

            var kotlinAdapter = new RecordingAdapter("Kotlin");
            var kotlinOrchestrator = new KotlinSemanticAnalysisOrchestrator(
                [kotlinAdapter],
                classpathResolver,
                NullLogger<KotlinSemanticAnalysisOrchestrator>.Instance,
                androidPlanner: planner);
            await kotlinOrchestrator.AnalyzeAsync(context);

            Assert.Equal(2, javaAdapter.Requests.Count);
            Assert.Equal(2, kotlinAdapter.Requests.Count);
            Assert.All(javaAdapter.Requests.Concat(kotlinAdapter.Requests), request =>
            {
                Assert.Equal("debug", request.ProviderOptions["android.variantName"]);
                Assert.Contains(Path.GetFullPath(androidJar), request.ClasspathEntries);
                Assert.True(request.ClasspathResolutionComplete);
            });
            Assert.Contains(javaAdapter.Requests, request => !request.IncludeTestCode
                && request.SourceFiles.Single().EndsWith("Main.java", StringComparison.Ordinal)
                && request.SourceLevel == "17");
            Assert.Contains(javaAdapter.Requests, request => request.IncludeTestCode
                && request.SourceFiles.Single().EndsWith("MainTest.java", StringComparison.Ordinal));
            Assert.Contains(kotlinAdapter.Requests, request => !request.IncludeTestCode
                && request.SourceFiles.Single().EndsWith("Main.kt", StringComparison.Ordinal)
                && request.ProviderOptions["kotlin.jvmTarget"] == "17");
            Assert.Contains(kotlinAdapter.Requests, request => request.IncludeTestCode
                && request.SourceFiles.Single().EndsWith("MainTest.kt", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static GradleAndroidSourceSet SourceSet(
        string name,
        ProjectSourceRootOwnership ownership,
        GradleAndroidTestKind testKind,
        string javaRoot,
        string kotlinRoot) =>
        new()
        {
            Name = name,
            Ownership = ownership,
            TestKind = testKind,
            AppliesToVariants = ["debug"],
            JavaSourceDirectories = [javaRoot],
            KotlinSourceDirectories = [kotlinRoot]
        };

    private sealed class EmptyClasspathResolver : IJvmClasspathResolver
    {
        public JvmClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode) =>
            new([], [], false, true);
    }

    private sealed class RecordingAdapter(string language) : ISemanticAnalysisAdapter
    {
        public string AdapterId => $"recording-{language.ToLowerInvariant()}";
        public string Language => language;
        public IReadOnlyCollection<string> SupportedCapabilities =>
            ["symbols", "relationships", "compiler-diagnostics", "jvm-interop"];
        public List<SemanticAnalysisRequest> Requests { get; } = [];

        public bool CanAnalyze(SemanticAnalysisRequest request) =>
            request.Language.Equals(language, StringComparison.OrdinalIgnoreCase);

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var now = DateTimeOffset.UtcNow;
            return ValueTask.FromResult(new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = language,
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                StartedAtUtc = now,
                CompletedAtUtc = now
            });
        }
    }
}
