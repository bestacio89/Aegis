using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JavaProjectDependencyGraphTests
{
    [Fact]
    public void Maven_project_dependencies_become_module_edges_with_test_scope_preserved()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var common = Path.Combine(root, "common");
        var application = Path.Combine(root, "application");
        var applicationTests = Path.Combine(root, "application-tests");
        Directory.CreateDirectory(Path.Combine(common, "src", "main", "java", "sample"));
        Directory.CreateDirectory(Path.Combine(application, "src", "main", "java", "sample"));
        Directory.CreateDirectory(Path.Combine(applicationTests, "src", "test", "java", "sample"));

        try
        {
            Write(root, "pom.xml", """
                <project xmlns="http://maven.apache.org/POM/4.0.0">
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>sample</groupId><artifactId>root</artifactId><version>1.0.0</version>
                  <packaging>pom</packaging>
                  <modules><module>common</module><module>application</module><module>application-tests</module></modules>
                </project>
                """);
            Write(common, "pom.xml", ModulePom("common", ""));
            Write(application, "pom.xml", ModulePom("application", """
                  <dependencies>
                    <dependency><groupId>sample</groupId><artifactId>common</artifactId><version>1.0.0</version></dependency>
                  </dependencies>
                """));
            Write(applicationTests, "pom.xml", ModulePom("application-tests", """
                  <dependencies>
                    <dependency><groupId>sample</groupId><artifactId>application</artifactId><version>1.0.0</version><scope>test</scope></dependency>
                  </dependencies>
                """));
            Write(Path.Combine(common, "src", "main", "java", "sample"), "Common.java", "package sample; public class Common {}");
            Write(Path.Combine(application, "src", "main", "java", "sample"), "Application.java", "package sample; public class Application {}");
            Write(Path.Combine(applicationTests, "src", "test", "java", "sample"), "ApplicationTests.java", "package sample; public class ApplicationTests {}");

            var context = ProjectArchitectureContextDetector.Detect(root);

            Assert.Contains(context.Dependencies, dependency =>
                dependency.DependencyType == "MavenProjectDependency"
                && Path.GetFullPath(dependency.SourceProjectPath) == Path.GetFullPath(application)
                && Path.GetFullPath(dependency.TargetProjectPath) == Path.GetFullPath(common)
                && dependency.Scope == "Production");
            Assert.Contains(context.Dependencies, dependency =>
                dependency.DependencyType == "MavenProjectDependency"
                && Path.GetFullPath(dependency.SourceProjectPath) == Path.GetFullPath(applicationTests)
                && Path.GetFullPath(dependency.TargetProjectPath) == Path.GetFullPath(application)
                && dependency.Scope == "Test");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Gradle_project_dependencies_become_module_edges_with_test_scope_preserved()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var api = Path.Combine(root, "api");
        var service = Path.Combine(root, "service");
        var integrationTests = Path.Combine(root, "integration-tests");
        Directory.CreateDirectory(Path.Combine(api, "src", "main", "java", "sample"));
        Directory.CreateDirectory(Path.Combine(service, "src", "main", "java", "sample"));
        Directory.CreateDirectory(Path.Combine(integrationTests, "src", "test", "java", "sample"));

        try
        {
            Write(root, "settings.gradle.kts", """
                rootProject.name = "sample"
                include(":api", ":service", ":integration-tests")
                """);
            Write(root, "build.gradle.kts", """
                plugins { java }
                group = "sample"
                version = "1.0.0"
                """);
            Write(api, "build.gradle.kts", "plugins { java }");
            Write(service, "build.gradle.kts", """
                plugins { java }
                dependencies { implementation(project(":api")) }
                """);
            Write(integrationTests, "build.gradle.kts", """
                plugins { java }
                dependencies { testImplementation(project(":service")) }
                """);
            Write(Path.Combine(api, "src", "main", "java", "sample"), "Api.java", "package sample; public class Api {}");
            Write(Path.Combine(service, "src", "main", "java", "sample"), "Service.java", "package sample; public class Service {}");
            Write(Path.Combine(integrationTests, "src", "test", "java", "sample"), "ServiceTests.java", "package sample; public class ServiceTests {}");

            var context = ProjectArchitectureContextDetector.Detect(root);

            Assert.Contains(context.Dependencies, dependency =>
                dependency.DependencyType == "GradleProjectDependency"
                && Path.GetFullPath(dependency.SourceProjectPath) == Path.GetFullPath(service)
                && Path.GetFullPath(dependency.TargetProjectPath) == Path.GetFullPath(api)
                && dependency.Scope == "Production");
            Assert.Contains(context.Dependencies, dependency =>
                dependency.DependencyType == "GradleProjectDependency"
                && Path.GetFullPath(dependency.SourceProjectPath) == Path.GetFullPath(integrationTests)
                && Path.GetFullPath(dependency.TargetProjectPath) == Path.GetFullPath(service)
                && dependency.Scope == "Test");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ModulePom(string artifact, string dependencies) => $$"""
        <project xmlns="http://maven.apache.org/POM/4.0.0">
          <modelVersion>4.0.0</modelVersion>
          <groupId>sample</groupId><artifactId>{{artifact}}</artifactId><version>1.0.0</version>
          <packaging>jar</packaging>
          {{dependencies}}
        </project>
        """;

    private static void Write(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
    }
}
