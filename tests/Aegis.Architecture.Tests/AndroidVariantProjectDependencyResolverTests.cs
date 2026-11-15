using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class AndroidVariantProjectDependencyResolverTests
{
    [Fact]
    public void Resolves_project_paths_only_within_the_consumers_owning_gradle_build()
    {
        var root = Module("root");
        var app = Module("app");
        var library = Module("library");
        var otherRoot = Module("other-root");
        var otherLibrary = Module("other-library");
        root.GradleModel = BuildModel(root, (":", root.Path), (":app", app.Path), (":library", library.Path));
        otherRoot.GradleModel = BuildModel(otherRoot, (":", otherRoot.Path), (":library", otherLibrary.Path));
        app.GradleBuildModel = AndroidBuildModel();
        library.GradleBuildModel = AndroidBuildModel();

        var selection = Selection(
            Dependency("debugImplementation", ":library"));

        var result = new AndroidVariantProjectDependencyResolver().Resolve(
            app,
            [root, app, library, otherRoot, otherLibrary],
            selection);

        Assert.True(result.IsComplete);
        Assert.Equal([Path.GetFullPath(library.Path)], result.AndroidProductionDependencies);
        Assert.Empty(result.JvmProductionDependencies);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Rejects_missing_or_ambiguous_project_targets_without_guessing()
    {
        var root = Module("root");
        var app = Module("app");
        root.GradleModel = BuildModel(root, (":", root.Path), (":app", app.Path));
        app.GradleBuildModel = AndroidBuildModel();

        var missing = new AndroidVariantProjectDependencyResolver().Resolve(
            app,
            [root, app],
            Selection(Dependency("debugImplementation", ":missing")));

        Assert.False(missing.IsComplete);
        Assert.Contains(missing.Diagnostics, diagnostic =>
            diagnostic.Code == "AndroidProjectDependencyTargetNotDeclared");

        var library = Module("library");
        var duplicate = Module("library-duplicate");
        library.Path = Path.Combine(root.Path, "library");
        duplicate.Path = library.Path;
        root.GradleModel = BuildModel(root, (":", root.Path), (":app", app.Path), (":library", library.Path));
        library.GradleBuildModel = AndroidBuildModel();
        duplicate.GradleBuildModel = AndroidBuildModel();

        var ambiguous = new AndroidVariantProjectDependencyResolver().Resolve(
            app,
            [root, app, library, duplicate],
            Selection(Dependency("debugImplementation", ":library")));

        Assert.False(ambiguous.IsComplete);
        Assert.Contains(ambiguous.Diagnostics, diagnostic =>
            diagnostic.Code == "AndroidProjectDependencyModuleAmbiguous");
    }

    [Fact]
    public void Requires_exact_compatible_android_variant_and_separates_jvm_and_test_outputs()
    {
        var root = Module("root");
        var app = Module("app");
        var androidLibrary = Module("android-library");
        var jvmLibrary = Module("jvm-library");
        root.GradleModel = BuildModel(
            root,
            (":", root.Path),
            (":app", app.Path),
            (":android-library", androidLibrary.Path),
            (":jvm-library", jvmLibrary.Path));
        app.GradleBuildModel = AndroidBuildModel();
        androidLibrary.GradleBuildModel = AndroidBuildModel("debug", "release");
        jvmLibrary.GradleBuildModel = new GradleBuildModel { IsComplete = true };

        var selection = Selection(
            Dependency("debugImplementation", ":android-library"),
            Dependency("debugUnitTestImplementation", ":jvm-library"));

        var result = new AndroidVariantProjectDependencyResolver().Resolve(
            app,
            [root, app, androidLibrary, jvmLibrary],
            selection);

        Assert.True(result.IsComplete);
        Assert.Equal([Path.GetFullPath(androidLibrary.Path)], result.AndroidProductionDependencies);
        Assert.Equal([Path.GetFullPath(jvmLibrary.Path)], result.JvmTestDependencies);
        Assert.Empty(result.JvmProductionDependencies);
        Assert.Empty(result.AndroidTestDependencies);

        androidLibrary.GradleBuildModel = AndroidBuildModel("release");
        var incompatible = new AndroidVariantProjectDependencyResolver().Resolve(
            app,
            [root, app, androidLibrary, jvmLibrary],
            Selection(Dependency("debugImplementation", ":android-library")));

        Assert.False(incompatible.IsComplete);
        Assert.Contains(incompatible.Diagnostics, diagnostic =>
            diagnostic.Code == "AndroidProjectDependencyVariantIncompatible");
    }

    [Fact]
    public async Task Assembles_only_registered_outputs_in_selected_variant_and_dependency_order()
    {
        var root = Module("root");
        var app = Module("app");
        var androidLibrary = Module("android-library");
        var jvmLibrary = Module("jvm-library");
        root.GradleModel = BuildModel(
            root,
            (":", root.Path),
            (":app", app.Path),
            (":android-library", androidLibrary.Path),
            (":jvm-library", jvmLibrary.Path));
        app.GradleBuildModel = AndroidBuildModel();
        androidLibrary.GradleBuildModel = AndroidBuildModel("debug", "release");
        jvmLibrary.GradleBuildModel = new GradleBuildModel { IsComplete = true };

        var selection = Selection(
            Dependency("debugImplementation", ":android-library"),
            Dependency("debugUnitTestImplementation", ":jvm-library"));
        var resolver = new AndroidVariantProjectDependencyResolver();
        var resolution = resolver.Resolve(app, [root, app, androidLibrary, jvmLibrary], selection);

        await using var executionContext = new SemanticAnalysisExecutionContext(
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var appOutput = RegisterOutput(executionContext, app.Path, false, "debug");
        var androidDebugOutput = RegisterOutput(executionContext, androidLibrary.Path, false, "debug");
        _ = RegisterOutput(executionContext, androidLibrary.Path, false, "release");
        var jvmOutput = RegisterOutput(executionContext, jvmLibrary.Path, false);

        var classpath = resolver.AssembleCompilationOutputs(
            app,
            resolution,
            executionContext,
            includeTestCode: true,
            variantName: "debug");

        Assert.True(classpath.IsComplete);
        Assert.Equal([appOutput, androidDebugOutput, jvmOutput], classpath.Entries);
        Assert.Empty(classpath.Diagnostics);
    }

    private static string RegisterOutput(
        SemanticAnalysisExecutionContext context,
        string modulePath,
        bool isTestCode,
        string? variantName = null)
    {
        var artifactRoot = context.CreateArtifactRoot();
        var output = Path.Combine(artifactRoot, "classes");
        Directory.CreateDirectory(output);
        context.RegisterCompilationOutput(modulePath, isTestCode, artifactRoot, output, variantName);
        return output;
    }

    [Fact]
    public void Keeps_project_dependency_resolution_partial_when_transitive_project_outputs_are_not_resolved()
    {
        var root = Module("root");
        var app = Module("app");
        var library = Module("library");
        root.GradleModel = BuildModel(root, (":", root.Path), (":app", app.Path), (":library", library.Path));
        app.GradleBuildModel = AndroidBuildModel();
        library.GradleBuildModel = new GradleBuildModel
        {
            IsComplete = true,
            Android = new GradleAndroidBuildModel
            {
                IsComplete = true,
                Variants = [new GradleAndroidVariant("debug", [], "debug")]
            },
            Dependencies = [Dependency("implementation", ":nested")]
        };

        var result = new AndroidVariantProjectDependencyResolver().Resolve(
            app,
            [root, app, library],
            Selection(Dependency("debugImplementation", ":library")));

        Assert.False(result.IsComplete);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "AndroidProjectDependencyTransitiveOutputsUnresolved");
        Assert.Equal([Path.GetFullPath(library.Path)], result.AndroidProductionDependencies);
    }

    [Fact]
    public void Reports_incomplete_owning_build_model_as_partial_evidence()
    {
        var root = Module("root");
        var app = Module("app");
        var library = Module("library");
        root.GradleModel = BuildModel(root, (":", root.Path), (":app", app.Path), (":library", library.Path));
        root.GradleModel!.IsComplete = false;
        app.GradleBuildModel = AndroidBuildModel();
        library.GradleBuildModel = AndroidBuildModel();

        var result = new AndroidVariantProjectDependencyResolver().Resolve(
            app,
            [root, app, library],
            Selection(Dependency("debugImplementation", ":library")));

        Assert.False(result.IsComplete);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "AndroidProjectDependencyBuildModelIncomplete");
        Assert.Equal([Path.GetFullPath(library.Path)], result.AndroidProductionDependencies);
    }

    private static ProjectModuleContext Module(string name) =>
        new()
        {
            Name = name,
            Path = Path.Combine(Path.GetTempPath(), "aegis-android-project-dependencies", name),
            BuildSystem = ArchitectureBuildSystem.Gradle
        };

    private static GradleProjectModel BuildModel(
        ProjectModuleContext root,
        params (string ProjectPath, string ProjectDirectory)[] projects) =>
        new()
        {
            RootDirectory = root.Path,
            SettingsFile = Path.Combine(root.Path, "settings.gradle.kts"),
            Projects = projects.Select(project => new GradleProjectModel.ProjectReference(
                project.ProjectPath,
                project.ProjectDirectory,
                null,
                project.ProjectPath == ":")).ToList(),
            IsComplete = true
        };

    private static GradleBuildModel AndroidBuildModel(params string[] variants) =>
        new()
        {
            IsComplete = true,
            Android = new GradleAndroidBuildModel
            {
                IsComplete = true,
                Variants = variants.Length == 0
                    ? [new GradleAndroidVariant("debug", [], "debug")]
                    : variants.Select(variant => new GradleAndroidVariant(variant, [], variant)).ToList()
            }
        };

    private static AndroidVariantDependencySelection Selection(
        params GradleBuildModel.Dependency[] dependencies) =>
        new("debug", AndroidVariantDependencyScope.UnitTest, dependencies, [], true);

    private static GradleBuildModel.Dependency Dependency(string configuration, string projectPath) =>
        new(configuration, null, null, null, projectPath, null, false, false, false, $"project('{projectPath}')");
}
