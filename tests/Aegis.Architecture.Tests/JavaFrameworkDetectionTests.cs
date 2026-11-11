using Aegis.Architecture.Analysis;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JavaFrameworkDetectionTests
{
    [Fact]
    public void Detect_uses_module_owned_maven_coordinates_instead_of_path_names()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisJavaFramework_{Guid.NewGuid():N}");
        var service = Path.Combine(root, "spring-boot-service");
        Directory.CreateDirectory(service);
        try
        {
            File.WriteAllText(Path.Combine(service, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>service</artifactId>
                  <version>1.0.0</version>
                  <dependencies>
                    <dependency>
                      <groupId>org.example</groupId>
                      <artifactId>service-contracts</artifactId>
                      <version>1.0.0</version>
                    </dependency>
                  </dependencies>
                </project>
                """);
            var source = Path.Combine(service, "src", "main", "java", "example", "Service.java");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package example; public final class Service { }");

            var context = ProjectArchitectureContextDetector.Detect(root);

            var module = Assert.Single(context.Modules);
            Assert.Equal("Java SE", module.Framework);
            Assert.DoesNotContain(module.Frameworks, framework => framework == "Spring Boot");
            Assert.DoesNotContain(context.DetectedFrameworks, framework => framework == "Spring Boot");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_classifies_spring_boot_from_module_owned_dependency_evidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisJavaSpring_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>spring-service</artifactId>
                  <version>1.0.0</version>
                  <dependencies>
                    <dependency>
                      <groupId>org.springframework.boot</groupId>
                      <artifactId>spring-boot-starter-web</artifactId>
                      <version>3.4.1</version>
                    </dependency>
                    <dependency>
                      <groupId>org.hibernate.orm</groupId>
                      <artifactId>hibernate-core</artifactId>
                      <version>6.6.8.Final</version>
                    </dependency>
                  </dependencies>
                </project>
                """);
            var source = Path.Combine(root, "src", "main", "java", "example", "Service.java");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package example; public final class Service { }");

            var context = ProjectArchitectureContextDetector.Detect(root);

            var module = Assert.Single(context.Modules);
            Assert.Equal("Spring Boot", module.Framework);
            Assert.Contains(module.Frameworks, framework => framework == "Spring Boot");
            Assert.Contains(module.Frameworks, framework => framework == "Hibernate");
            Assert.Contains(context.DetectedFrameworks, framework => framework == "Spring Boot");
            Assert.Contains(context.DetectedFrameworks, framework => framework == "Hibernate");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
