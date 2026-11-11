using Aegis.Architecture.Analysis;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class GradleGroovyBuildModelTests
{
    [Fact]
    public void Detect_extracts_common_groovy_plugin_dependency_and_project_notation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleGroovy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle"),
                "rootProject.name = 'groovy-sample'\ninclude ':api'");
            File.WriteAllText(Path.Combine(root, "build.gradle"), """
                plugins {
                    id 'java'
                    id 'org.springframework.boot' version '3.4.1'
                }

                java {
                    toolchain {
                        languageVersion = JavaLanguageVersion.of(21)
                    }
                }

                dependencies {
                    implementation 'org.springframework:spring-context:6.2.8'
                    implementation platform('org.springframework.boot:spring-boot-dependencies:3.4.1')
                    implementation project(':api')
                    testImplementation 'org.junit.jupiter:junit-jupiter-api:5.12.2'
                    annotationProcessor 'org.mapstruct:mapstruct-processor:1.6.3'
                }
                """);

            var source = Path.Combine(root, "src", "main", "java", "example", "Application.java");
            var testSource = Path.Combine(root, "src", "test", "java", "example", "ApplicationTest.java");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(testSource)!);
            File.WriteAllText(source, "package example; public final class Application { }");
            File.WriteAllText(testSource, "package example; public final class ApplicationTest { }");

            var context = ProjectArchitectureContextDetector.Detect(root);

            var module = Assert.Single(context.Modules, candidate => candidate.Path == root);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);
            Assert.Equal("Groovy", model.Dialect);
            Assert.Equal("21", model.JavaToolchainVersion);
            Assert.Contains(model.Plugins, plugin => plugin.Id == "java");
            Assert.Contains(model.Plugins, plugin =>
                plugin.Id == "org.springframework.boot" && plugin.Version == "3.4.1");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Group == "org.springframework" && dependency.Artifact == "spring-context");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.IsPlatform && dependency.Group == "org.springframework.boot");
            Assert.Contains(model.Dependencies, dependency => dependency.ProjectPath == ":api");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Configuration == "annotationProcessor" && dependency.IsAnnotationProcessor);
            Assert.Contains(context.DependencyEvidence, dependency =>
                dependency.Name == "org.junit.jupiter:junit-jupiter-api" && dependency.IsTestOnly);
            Assert.Contains(model.SourceSets, sourceSet => sourceSet.Name == "main");
            Assert.Contains(model.SourceSets, sourceSet => sourceSet.Name == "test" && sourceSet.IsTest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
