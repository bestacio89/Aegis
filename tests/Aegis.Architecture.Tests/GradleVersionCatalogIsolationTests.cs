using Aegis.Architecture.Analysis;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class GradleVersionCatalogIsolationTests
{
    [Fact]
    public void Detect_keeps_version_catalog_aliases_scoped_to_their_gradle_build_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleCatalogIsolation_{Guid.NewGuid():N}");
        var firstBuild = Path.Combine(root, "first");
        var secondBuild = Path.Combine(root, "second");
        Directory.CreateDirectory(Path.Combine(firstBuild, "gradle"));
        Directory.CreateDirectory(Path.Combine(secondBuild, "gradle"));
        try
        {
            WriteBuild(firstBuild, "first-build", "org.example.first", "1.2.3");
            WriteBuild(secondBuild, "second-build", "org.example.second", "4.5.6");

            var context = ProjectArchitectureContextDetector.Detect(root);

            var firstModule = Assert.Single(context.Modules, module => module.Path == firstBuild);
            var secondModule = Assert.Single(context.Modules, module => module.Path == secondBuild);
            var firstModel = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(
                firstModule.GradleBuildModel);
            var secondModel = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(
                secondModule.GradleBuildModel);

            var firstDependency = Assert.Single(firstModel.Dependencies);
            var secondDependency = Assert.Single(secondModel.Dependencies);
            Assert.Equal("org.example.first", firstDependency.Group);
            Assert.Equal("shared", firstDependency.Artifact);
            Assert.Equal("1.2.3", firstDependency.Version);
            Assert.Equal("org.example.second", secondDependency.Group);
            Assert.Equal("shared", secondDependency.Artifact);
            Assert.Equal("4.5.6", secondDependency.Version);
            Assert.True(firstModel.IsComplete);
            Assert.True(secondModel.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteBuild(
        string directory,
        string rootProjectName,
        string group,
        string version)
    {
        File.WriteAllText(Path.Combine(directory, "settings.gradle.kts"),
            $"rootProject.name = \"{rootProjectName}\"");
        File.WriteAllText(Path.Combine(directory, "build.gradle.kts"), """
            dependencies {
                implementation(libs.shared)
            }
            """);
        File.WriteAllText(Path.Combine(directory, "gradle", "libs.versions.toml"),
            $"""
            [libraries]
            shared = "{group}:shared:{version}"
            """);

        var source = Path.Combine(directory, "src", "main", "java", "example", "Application.java");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "package example; public final class Application { }");
    }
}
