using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Enums;
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

    [Fact]
    public void Detect_classifies_additional_java_architecture_integrations_with_version_and_provenance()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisJavaIntegrations_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>integration-service</artifactId>
                  <version>1.0.0</version>
                  <dependencies>
                    <dependency><groupId>org.apache.kafka</groupId><artifactId>kafka-clients</artifactId><version>3.9.0</version></dependency>
                    <dependency><groupId>com.rabbitmq</groupId><artifactId>amqp-client</artifactId><version>5.24.0</version></dependency>
                    <dependency><groupId>io.grpc</groupId><artifactId>grpc-stub</artifactId><version>1.69.0</version></dependency>
                    <dependency><groupId>com.google.protobuf</groupId><artifactId>protobuf-java</artifactId><version>4.29.3</version></dependency>
                    <dependency><groupId>org.mapstruct</groupId><artifactId>mapstruct</artifactId><version>1.6.3</version></dependency>
                    <dependency><groupId>org.projectlombok</groupId><artifactId>lombok</artifactId><version>1.18.36</version></dependency>
                    <dependency><groupId>com.fasterxml.jackson.core</groupId><artifactId>jackson-databind</artifactId><version>2.18.2</version></dependency>
                    <dependency><groupId>org.springdoc</groupId><artifactId>springdoc-openapi-starter-webmvc-ui</artifactId><version>2.8.3</version></dependency>
                    <dependency><groupId>org.flywaydb</groupId><artifactId>flyway-core</artifactId><version>11.3.3</version></dependency>
                    <dependency><groupId>org.liquibase</groupId><artifactId>liquibase-core</artifactId><version>4.31.0</version></dependency>
                    <dependency><groupId>org.eclipse.persistence</groupId><artifactId>eclipselink</artifactId><version>4.0.5</version></dependency>
                  </dependencies>
                </project>
                """);
            var source = Path.Combine(root, "src", "main", "java", "example", "Service.java");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package example; public final class Service { }");

            var context = ProjectArchitectureContextDetector.Detect(root);

            var module = Assert.Single(context.Modules);
            var expected = new Dictionary<string, (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind Kind, string Version)>
            {
                ["Apache Kafka"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.Messaging, "3.9.0"),
                ["RabbitMQ"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.Messaging, "5.24.0"),
                ["gRPC"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.Messaging, "1.69.0"),
                ["Protocol Buffers"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.Serialization, "4.29.3"),
                ["MapStruct"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.Application, "1.6.3"),
                ["Lombok"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.BuildPlugin, "1.18.36"),
                ["Jackson"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.Serialization, "2.18.2"),
                ["OpenAPI"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.Web, "2.8.3"),
                ["Flyway"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.Persistence, "11.3.3"),
                ["Liquibase"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.Persistence, "4.31.0"),
                ["EclipseLink"] = (Aegis.Shared.Architecture.Enums.FrameworkEvidenceKind.Persistence, "4.0.5")
            };

            foreach (var (name, expectedEvidence) in expected)
            {
                var evidence = Assert.Single(module.FrameworkEvidence.Where(item => item.Name == name));
                Assert.Equal(expectedEvidence.Kind, evidence.Kind);
                Assert.Equal(expectedEvidence.Version, evidence.Version);
                Assert.False(evidence.IsTestOnly);
                Assert.Equal(Aegis.Shared.Architecture.Enums.SemanticEvidenceSource.BuildModel,
                    Assert.Single(evidence.Provenance).Source);
                Assert.Contains(context.FrameworkEvidence, item => item.Id == evidence.Id);
                Assert.Contains(context.DetectedFrameworks, item => item == name);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void Detect_classifies_java_test_frameworks_and_maven_test_plugins_as_test_only_evidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisJavaTestFrameworks_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>test-framework-service</artifactId>
                  <version>1.0.0</version>
                  <dependencies>
                    <dependency><groupId>junit</groupId><artifactId>junit</artifactId><version>4.13.2</version><scope>test</scope></dependency>
                    <dependency><groupId>org.junit.jupiter</groupId><artifactId>junit-jupiter-api</artifactId><version>5.12.2</version><scope>test</scope></dependency>
                    <dependency><groupId>org.junit.platform</groupId><artifactId>junit-platform-launcher</artifactId><version>1.12.2</version><scope>test</scope></dependency>
                    <dependency><groupId>org.junit.vintage</groupId><artifactId>junit-vintage-engine</artifactId><version>5.12.2</version><scope>test</scope></dependency>
                    <dependency><groupId>org.testng</groupId><artifactId>testng</artifactId><version>7.10.2</version><scope>test</scope></dependency>
                    <dependency><groupId>org.mockito</groupId><artifactId>mockito-junit-jupiter</artifactId><version>5.15.2</version><scope>test</scope></dependency>
                    <dependency><groupId>org.assertj</groupId><artifactId>assertj-core</artifactId><version>3.27.3</version><scope>test</scope></dependency>
                    <dependency><groupId>org.testcontainers</groupId><artifactId>junit-jupiter</artifactId><version>1.20.4</version><scope>test</scope></dependency>
                  </dependencies>
                  <build>
                    <plugins>
                      <plugin>
                        <groupId>org.apache.maven.plugins</groupId>
                        <artifactId>maven-surefire-plugin</artifactId>
                        <version>3.5.2</version>
                      </plugin>
                      <plugin>
                        <groupId>org.apache.maven.plugins</groupId>
                        <artifactId>maven-failsafe-plugin</artifactId>
                        <version>3.5.2</version>
                      </plugin>
                    </plugins>
                  </build>
                </project>
                """);
            var source = Path.Combine(root, "src", "main", "java", "example", "Service.java");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package example; public final class Service { }");

            var context = ProjectArchitectureContextDetector.Detect(root);

            var module = Assert.Single(context.Modules);
            var expectedVersions = new Dictionary<string, string[]>
            {
                ["JUnit 4"] = ["4.13.2"],
                ["JUnit 5"] = ["5.12.2", "1.12.2"],
                ["TestNG"] = ["7.10.2"],
                ["Mockito"] = ["5.15.2"],
                ["AssertJ"] = ["3.27.3"],
                ["Testcontainers"] = ["1.20.4"],
                ["Maven Surefire"] = ["3.5.2"],
                ["Maven Failsafe"] = ["3.5.2"]
            };

            foreach (var (name, versions) in expectedVersions)
            {
                foreach (var version in versions)
                {
                    var evidence = Assert.Single(module.FrameworkEvidence.Where(item =>
                        item.Name == name && item.Version == version));
                    Assert.Equal(FrameworkEvidenceKind.Testing, evidence.Kind);
                    Assert.True(evidence.IsTestOnly);
                    Assert.Equal(SemanticEvidenceSource.BuildModel, Assert.Single(evidence.Provenance).Source);
                    Assert.Contains(context.FrameworkEvidence, item => item.Id == evidence.Id);
                }
            }

            Assert.DoesNotContain(module.FrameworkEvidence.Where(item =>
                item.Name is "JUnit 4" or "JUnit 5" or "TestNG" or "Mockito" or "AssertJ" or "Testcontainers"),
                item => !item.IsTestOnly);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

}