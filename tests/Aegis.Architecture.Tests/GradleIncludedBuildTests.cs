using Aegis.Architecture.Analysis;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class GradleIncludedBuildTests
{
    [Fact]
    public void Detect_keeps_included_builds_independent_and_resolves_shared_membership_and_catalog_ownership()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleComposite_{Guid.NewGuid():N}");
        var buildLogic = Path.Combine(root, "build-logic");
        var tooling = Path.Combine(root, "tooling");
        Directory.CreateDirectory(root);
        try
        {
            WriteFile(root, "settings.gradle.kts", """
                rootProject.name = "main"
                include(":app")
                includeBuild("build-logic")
                """);
            WriteFile(root, "build.gradle.kts", """
                dependencies {
                    implementation(libs.parent)
                }
                """);
            WriteFile(root, "app/build.gradle.kts", "");
            WriteFile(root, "gradle/libs.versions.toml", """
                [libraries]
                parent = { module = "org.parent:parent-lib", version = "1.0" }
                """);

            WriteFile(root, "build-logic/settings.gradle.kts", """
                rootProject.name = "build-logic"
                include(":conventions")
                """);
            WriteFile(root, "build-logic/build.gradle.kts", "");
            WriteFile(root, "build-logic/conventions/build.gradle.kts", """
                dependencies {
                    implementation(libs.included)
                }
                """);
            WriteFile(root, "build-logic/gradle/libs.versions.toml", """
                [libraries]
                included = { module = "org.included:included-lib", version = "2.0" }
                """);

            WriteFile(root, "tooling/settings.gradle.kts", """
                rootProject.name = "tooling"
                includeBuild("../build-logic")
                """);
            WriteFile(root, "tooling/build.gradle.kts", "");

            var context = ProjectArchitectureContextDetector.Detect(root);

            var mainModule = Assert.Single(context.Modules, module => module.Path == root);
            var mainModel = Assert.IsType<Aegis.Shared.Architecture.Models.GradleProjectModel>(
                mainModule.GradleModel);
            var mainIncludedBuild = Assert.Single(mainModel.IncludedBuilds);
            Assert.True(mainIncludedBuild.IsDiscovered);
            Assert.Equal(Path.GetFullPath(buildLogic), mainIncludedBuild.Directory);
            Assert.Equal(Path.Combine(buildLogic, "settings.gradle.kts"), mainIncludedBuild.SettingsFile);
            Assert.Equal("build-logic", mainIncludedBuild.RootProjectName);
            Assert.Contains(mainModel.Projects, project => project.ProjectPath == ":app");
            Assert.DoesNotContain(mainModel.Projects, project =>
                Path.GetFullPath(project.ProjectDirectory) == Path.GetFullPath(buildLogic));

            var toolingModule = Assert.Single(context.Modules, module => module.Path == tooling);
            var toolingModel = Assert.IsType<Aegis.Shared.Architecture.Models.GradleProjectModel>(
                toolingModule.GradleModel);
            var sharedMembership = Assert.Single(toolingModel.IncludedBuilds);
            Assert.True(sharedMembership.IsDiscovered);
            Assert.Equal(mainIncludedBuild.Directory, sharedMembership.Directory);

            var buildLogicModule = Assert.Single(context.Modules, module => module.Path == buildLogic);
            Assert.NotNull(buildLogicModule.GradleModel);
            var conventionsModule = Assert.Single(context.Modules, module =>
                module.Path == Path.Combine(buildLogic, "conventions"));
            Assert.Contains(conventionsModule.GradleBuildModel!.Dependencies, dependency =>
                dependency.Group == "org.included"
                && dependency.Artifact == "included-lib"
                && dependency.Version == "2.0");
            Assert.DoesNotContain(conventionsModule.GradleBuildModel.Dependencies, dependency =>
                dependency.Group == "org.parent");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_reports_missing_included_build_without_marking_the_settings_model_complete()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleMissingComposite_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            WriteFile(root, "settings.gradle", "rootProject.name = 'main'\nincludeBuild('missing-build')");
            WriteFile(root, "build.gradle", "");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleProjectModel>(module.GradleModel);

            Assert.False(model.IsComplete);
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleIncludedBuildDirectoryMissing");
            Assert.False(Assert.Single(model.IncludedBuilds).IsDiscovered);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_does_not_inspect_included_builds_outside_the_analysis_root()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"AegisGradleExternalComposite_{Guid.NewGuid():N}");
        var root = Path.Combine(temporaryRoot, "repository");
        var externalBuild = Path.Combine(temporaryRoot, "external-build");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(externalBuild);
        try
        {
            WriteFile(root, "settings.gradle.kts", """
                rootProject.name = "main"
                includeBuild("../external-build")
                """);
            WriteFile(root, "build.gradle.kts", "");
            WriteFile(externalBuild, "settings.gradle.kts", "rootProject.name = \"external\"");
            WriteFile(externalBuild, "build.gradle.kts", "");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleProjectModel>(module.GradleModel);

            Assert.False(model.IsComplete);
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleIncludedBuildOutsideAnalysisRoot");
            Assert.False(Assert.Single(model.IncludedBuilds).IsDiscovered);
            Assert.DoesNotContain(context.Modules, candidate =>
                Path.GetFullPath(candidate.Path) == Path.GetFullPath(externalBuild));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void Detect_prevents_an_included_build_from_becoming_a_parent_project()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleOverlappingComposite_{Guid.NewGuid():N}");
        var includedBuild = Path.Combine(root, "build-logic");
        Directory.CreateDirectory(root);
        try
        {
            WriteFile(root, "settings.gradle.kts", """
                rootProject.name = "main"
                include(":build-logic")
                includeBuild("build-logic")
                """);
            WriteFile(root, "build.gradle.kts", "");
            WriteFile(root, "build-logic/settings.gradle.kts", "rootProject.name = \"build-logic\"");
            WriteFile(root, "build-logic/build.gradle.kts", "");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var rootModule = Assert.Single(context.Modules, module => module.Path == root);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleProjectModel>(
                rootModule.GradleModel);

            Assert.DoesNotContain(model.Projects, project =>
                Path.GetFullPath(project.ProjectDirectory) == Path.GetFullPath(includedBuild));
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleProjectOverlapsIncludedBuild");
            Assert.True(Assert.Single(model.IncludedBuilds).IsDiscovered);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
