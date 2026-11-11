using Aegis.Architecture.Analysis;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class GradleBuildModelTests
{
    [Fact]
    public void Detect_extracts_literal_plugins_dependencies_catalog_and_toolchain_without_execution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleModel_{Guid.NewGuid():N}");
        var service = Path.Combine(root, "service");
        var catalogDirectory = Path.Combine(root, "gradle");
        Directory.CreateDirectory(service);
        Directory.CreateDirectory(catalogDirectory);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"sample\"\ninclude(\":service\")");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"),
                "plugins { id(\"java\") }");
            var buildFile = Path.Combine(service, "build.gradle.kts");
            File.WriteAllText(buildFile, """
                plugins {
                    id("org.springframework.boot") version "3.4.1"
                    alias(libs.plugins.mapstruct)
                }
                java {
                    toolchain {
                        languageVersion.set(JavaLanguageVersion.of(21))
                    }
                }
                dependencies {
                    implementation("org.springframework:spring-context:6.2.8")
                    testImplementation("org.junit.jupiter:junit-jupiter-api:5.12.2")
                    annotationProcessor(libs.mapstruct)
                    implementation(platform("org.springframework.boot:spring-boot-dependencies:3.4.1"))
                    implementation(project(":api"))
                }
                """);
            File.WriteAllText(Path.Combine(catalogDirectory, "libs.versions.toml"),
                """
                [versions]
                mapstruct = "1.6.3"

                [libraries]
                mapstruct = { module = "org.mapstruct:mapstruct", version.ref = "mapstruct" }

                [plugins]
                mapstruct = { id = "org.mapstruct", version = "1.6.3" }
                """);
            var source = Path.Combine(service, "src", "main", "java", "example", "Service.java");
            var testSource = Path.Combine(service, "src", "test", "java", "example", "ServiceTest.java");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(testSource)!);
            File.WriteAllText(source, "package example; public final class Service {}");
            File.WriteAllText(testSource, "package example; public final class ServiceTest {}");

            var context = ProjectArchitectureContextDetector.Detect(root);

            var module = Assert.Single(context.Modules, candidate => candidate.Path == service);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);
            Assert.Equal("Kotlin", model.Dialect);
            Assert.Equal("21", model.JavaToolchainVersion);
            Assert.Contains(model.Plugins, plugin => plugin.Id == "org.springframework.boot" && plugin.Version == "3.4.1");
            Assert.Contains(model.Plugins, plugin => plugin.Id == "org.mapstruct" && plugin.Version == "1.6.3");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Group == "org.springframework" && dependency.Artifact == "spring-context");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Group == "org.junit.jupiter" && dependency.Artifact == "junit-jupiter-api"
                && dependency.Configuration == "testImplementation");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Group == "org.mapstruct" && dependency.Artifact == "mapstruct"
                && dependency.IsAnnotationProcessor);
            Assert.Contains(model.Dependencies, dependency => dependency.IsPlatform
                && dependency.Group == "org.springframework.boot");
            Assert.Contains(model.Dependencies, dependency => dependency.ProjectPath == ":api");
            Assert.Contains(model.SourceSets, sourceSet => sourceSet.Name == "main" && !sourceSet.IsTest);
            Assert.Contains(model.SourceSets, sourceSet => sourceSet.Name == "test" && sourceSet.IsTest);
            Assert.Contains(context.DependencyEvidence, dependency =>
                dependency.Ecosystem == "Gradle" && dependency.Name == "org.junit.jupiter:junit-jupiter-api"
                && dependency.IsTestOnly && dependency.ResolvedVersion is null);
            Assert.Contains("org.springframework:spring-context", context.DetectedDependencies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_marks_dynamic_dependency_declarations_as_incomplete()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleDynamic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle"), "rootProject.name = 'sample'");
            File.WriteAllText(Path.Combine(root, "build.gradle"),
                "dependencies {\n implementation(\"org.example:library:$version\")\n}");
            var source = Path.Combine(root, "src", "main", "java", "example", "App.java");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package example; public final class App {}");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            Assert.NotNull(module.GradleBuildModel);
            Assert.False(module.GradleBuildModel.IsComplete);
            Assert.Contains(module.GradleBuildModel.Diagnostics,
                diagnostic => diagnostic.Code == "GradleDependencyNotStaticallyResolved");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
