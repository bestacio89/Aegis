using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Enums;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JavaProjectDiscoveryTests
{
    [Fact]
    public void Detect_identifies_maven_from_a_case_insensitive_pom_manifest()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "POM.XML"), "<project><modelVersion>4.0.0</modelVersion><artifactId>maven-root</artifactId></project>");
            var source = Path.Combine(root, "src", "main", "java", "example", "App.JAVA");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package example; public final class App { }");

            var context = ProjectArchitectureContextDetector.Detect(root);

            Assert.Equal("Java", context.Language);
            Assert.Equal(ArchitectureBuildSystem.Maven, context.BuildSystem);
            var module = Assert.Single(context.Modules);
            Assert.Equal("maven-root", module.Name);
            Assert.Equal(ArchitectureBuildSystem.Maven, module.BuildSystem);
            Assert.Equal("JavaProject", module.ProjectKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_identifies_gradle_and_discovers_each_build_file_boundary()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"gradle-root\"\ninclude(\":api\", \":service\")");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), "plugins { java }");

            var apiRoot = Path.Combine(root, "api");
            var serviceRoot = Path.Combine(root, "service");
            Directory.CreateDirectory(apiRoot);
            Directory.CreateDirectory(serviceRoot);
            File.WriteAllText(Path.Combine(apiRoot, "build.gradle"), "apply plugin: 'java'");
            File.WriteAllText(Path.Combine(serviceRoot, "build.gradle.kts"), "plugins { java }");

            var apiSource = Path.Combine(apiRoot, "src", "main", "java", "example", "Api.java");
            var serviceSource = Path.Combine(serviceRoot, "src", "main", "java", "example", "Service.java");
            Directory.CreateDirectory(Path.GetDirectoryName(apiSource)!);
            Directory.CreateDirectory(Path.GetDirectoryName(serviceSource)!);
            File.WriteAllText(apiSource, "package example; public interface Api { }");
            File.WriteAllText(serviceSource, "package example; public final class Service { }");

            var context = ProjectArchitectureContextDetector.Detect(root);

            Assert.Equal("Java", context.Language);
            Assert.Equal(ArchitectureBuildSystem.Gradle, context.BuildSystem);
            Assert.Equal(3, context.Modules.Count);
            Assert.All(context.Modules, module =>
            {
                Assert.Equal(ArchitectureBuildSystem.Gradle, module.BuildSystem);
                Assert.Equal("JavaProject", module.ProjectKind);
                Assert.Contains("Java", module.Frameworks);
            });
            Assert.Contains(context.Modules, module => module.Name == "api" && module.Path == apiRoot);
            Assert.Contains(context.Modules, module => module.Name == "service" && module.Path == serviceRoot);
            var rootModule = Assert.Single(context.Modules, module => module.Path == root);
            Assert.Contains(rootModule.ProjectFiles, file =>
                Path.GetFileName(file).Equals("settings.gradle.kts", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(rootModule.ProjectFiles, file =>
                Path.GetFileName(file).Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_does_not_claim_a_build_system_for_source_only_java()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(root, "src", "App.java");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "public final class App { }");

            var context = ProjectArchitectureContextDetector.Detect(root);

            Assert.Equal("Java", context.Language);
            Assert.Equal(ArchitectureBuildSystem.Unknown, context.BuildSystem);
            Assert.Empty(context.Modules);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_does_not_choose_between_maven_and_gradle_when_both_are_present()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), "<project><modelVersion>4.0.0</modelVersion></project>");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), "plugins { java }");

            var context = ProjectArchitectureContextDetector.Detect(root);

            Assert.Equal("Java", context.Language);
            Assert.Equal(ArchitectureBuildSystem.Unknown, context.BuildSystem);
            var module = Assert.Single(context.Modules);
            Assert.Equal(ArchitectureBuildSystem.Unknown, module.BuildSystem);
            Assert.Equal("Unknown", module.ProjectKind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AegisJavaDiscovery_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
