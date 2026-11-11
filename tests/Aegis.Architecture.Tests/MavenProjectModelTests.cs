using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Enums;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class MavenProjectModelTests
{
    [Fact]
    public void Detect_resolves_local_parent_coordinates_properties_managed_versions_and_scopes()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var parentDirectory = Path.Combine(root, "parent");
            var serviceDirectory = Path.Combine(root, "service");
            Directory.CreateDirectory(parentDirectory);
            Directory.CreateDirectory(serviceDirectory);

            File.WriteAllText(Path.Combine(parentDirectory, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>fixture-parent</artifactId>
                  <version>2.4.0</version>
                  <packaging>pom</packaging>
                  <properties>
                    <revision>2.4.0</revision>
                    <junit.version>5.12.2</junit.version>
                    <spring.version>6.2.8</spring.version>
                  </properties>
                  <modules><module>../service</module></modules>
                  <dependencies><dependency><groupId>org.example</groupId><artifactId>parent-api</artifactId><version>1.0.0</version></dependency></dependencies>
                  <dependencyManagement>
                    <dependencies>
                      <dependency>
                        <groupId>org.junit.jupiter</groupId>
                        <artifactId>junit-jupiter</artifactId>
                        <version>${junit.version}</version>
                      </dependency>
                      <dependency>
                        <groupId>org.springframework</groupId>
                        <artifactId>spring-context</artifactId>
                        <version>${spring.version}</version>
                      </dependency>
                    </dependencies>
                  </dependencyManagement>
                </project>
                """);

            File.WriteAllText(Path.Combine(serviceDirectory, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <parent>
                    <groupId>org.example</groupId>
                    <artifactId>fixture-parent</artifactId>
                    <version>2.4.0</version>
                    <relativePath>../parent/pom.xml</relativePath>
                  </parent>
                  <artifactId>fixture-service</artifactId>
                  <properties><service.revision>${project.version}</service.revision></properties>
                  <dependencies>
                    <dependency>
                      <groupId>org.springframework</groupId>
                      <artifactId>spring-context</artifactId>
                    </dependency>
                    <dependency>
                      <groupId>org.junit.jupiter</groupId>
                      <artifactId>junit-jupiter</artifactId>
                      <scope>test</scope>
                    </dependency>
                    <dependency>
                      <groupId>org.example</groupId>
                      <artifactId>internal-api</artifactId>
                      <version>${service.revision}</version>
                      <scope>runtime</scope>
                      <exclusions>
                        <exclusion><groupId>org.example</groupId><artifactId>excluded</artifactId></exclusion>
                      </exclusions>
                    </dependency>
                  </dependencies>
                  <build>
                    <plugins>
                      <plugin>
                        <groupId>org.apache.maven.plugins</groupId>
                        <artifactId>maven-compiler-plugin</artifactId>
                        <version>3.13.0</version>
                        <configuration>
                          <annotationProcessorPaths>
                            <path>
                              <groupId>org.mapstruct</groupId>
                              <artifactId>mapstruct-processor</artifactId>
                              <version>1.6.3</version>
                            </path>
                          </annotationProcessorPaths>
                        </configuration>
                      </plugin>
                    </plugins>
                    <pluginManagement>
                      <plugins>
                        <plugin>
                          <groupId>org.example.build</groupId>
                          <artifactId>managed-codegen-plugin</artifactId>
                          <version>2.0.0</version>
                        </plugin>
                      </plugins>
                    </pluginManagement>
                  </build>
                </project>
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules, candidate => candidate.Name == "fixture-service");
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(module.MavenModel);

            Assert.Equal("org.example", model.ProjectCoordinates.GroupId);
            Assert.Equal("fixture-service", model.ProjectCoordinates.ArtifactId);
            Assert.Equal("2.4.0", model.ProjectCoordinates.Version);
            Assert.Equal("org.example", model.ParentCoordinates?.GroupId);
            Assert.Equal("fixture-parent", model.ParentCoordinates?.ArtifactId);
            Assert.Equal(Path.Combine(parentDirectory, "pom.xml"), model.ParentProjectFile);
            Assert.Equal("2.4.0", model.EffectiveProperties["service.revision"]);

            var inheritedDependency = Assert.Single(model.Dependencies, dependency => dependency.ArtifactId == "parent-api");
            Assert.True(inheritedDependency.IsInherited);
            Assert.Contains(context.DependencyEvidence, item =>
                item.Name == "org.example:parent-api"
                && !item.IsDirect
                && item.Provenance.Any(provenance => provenance.FilePath == Path.Combine(parentDirectory, "pom.xml")));

            var spring = Assert.Single(model.Dependencies, dependency =>
                dependency.ArtifactId == "spring-context");
            Assert.Equal("6.2.8", spring.Version);
            Assert.True(spring.IsVersionManaged);
            Assert.Equal("compile", spring.Scope);

            var junit = Assert.Single(model.Dependencies, dependency =>
                dependency.ArtifactId == "junit-jupiter");
            Assert.Equal("5.12.2", junit.Version);
            Assert.True(junit.IsVersionManaged);
            Assert.Equal("test", junit.Scope);

            var internalApi = Assert.Single(model.Dependencies, dependency =>
                dependency.ArtifactId == "internal-api");
            Assert.Equal("2.4.0", internalApi.Version);
            Assert.Equal("runtime", internalApi.Scope);
            Assert.Single(internalApi.Exclusions);

            Assert.Contains(model.Plugins, plugin =>
                plugin.ArtifactId == "maven-compiler-plugin" && plugin.Version == "3.13.0");
            Assert.DoesNotContain(model.Plugins, plugin =>
                plugin.ArtifactId == "managed-codegen-plugin");
            Assert.Contains(model.ManagedPlugins, plugin =>
                plugin.ArtifactId == "managed-codegen-plugin" && plugin.Version == "2.0.0");

            var processor = Assert.Single(model.AnnotationProcessorPaths);
            Assert.Equal("mapstruct-processor", processor.ArtifactId);
            Assert.Equal("1.6.3", processor.Version);

            var evidence = context.DependencyEvidence.Where(item =>
                item.SourceProjectPath == module.Path).ToArray();
            Assert.Contains(evidence, item =>
                item.Name == "org.springframework:spring-context"
                && item.DeclaredVersion == "6.2.8"
                && item.ResolvedVersion is null
                && item.ResolutionStatus == DependencyResolutionStatus.Declared);
            Assert.Contains(evidence, item =>
                item.Name == "org.junit.jupiter:junit-jupiter"
                && item.IsTestOnly
                && item.Scope == "test");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_reports_unresolved_parent_and_bom_without_inventing_effective_versions()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <parent>
                    <groupId>org.external</groupId>
                    <artifactId>missing-parent</artifactId>
                    <version>9.0.0</version>
                    <relativePath>../not-present/pom.xml</relativePath>
                  </parent>
                  <artifactId>fixture</artifactId>
                  <dependencyManagement>
                    <dependencies>
                      <dependency>
                        <groupId>org.external</groupId>
                        <artifactId>missing-bom</artifactId>
                        <version>1.0.0</version>
                        <type>pom</type>
                        <scope>import</scope>
                      </dependency>
                    </dependencies>
                  </dependencyManagement>
                  <profiles><profile><id>integration-only</id></profile></profiles>
                  <dependencies>
                    <dependency>
                      <groupId>org.external</groupId>
                      <artifactId>managed-by-missing-bom</artifactId>
                    </dependency>
                    <dependency>
                      <groupId>org.external</groupId>
                      <artifactId>unresolved-property-version</artifactId>
                      <version>${missing.version}</version>
                    </dependency>
                  </dependencies>
                </project>
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(module.MavenModel);

            Assert.False(model.IsComplete);
            Assert.Contains(model.Diagnostics, diagnostic => diagnostic.Code == "MavenParentNotResolved");
            Assert.Contains(model.Diagnostics, diagnostic => diagnostic.Code == "MavenBomNotResolved");
            Assert.Contains(model.Diagnostics, diagnostic => diagnostic.Code == "MavenProfilesNotEvaluated");
            var dependency = Assert.Single(model.Dependencies, item =>
                item.ArtifactId == "managed-by-missing-bom");
            Assert.Null(dependency.Version);
            Assert.Contains(context.DependencyEvidence, item =>
                item.Name == "org.external:managed-by-missing-bom"
                && item.ResolutionStatus == DependencyResolutionStatus.PartiallyResolved);
            Assert.Contains(context.DependencyEvidence, item =>
                item.Name == "org.external:unresolved-property-version"
                && item.ResolutionStatus == DependencyResolutionStatus.PartiallyResolved);
            Assert.Contains(context.DependencyEvidence, item =>
                item.Name == "org.external:missing-bom"
                && item.Kind == DependencyEvidenceKind.Platform
                && item.Scope == "import");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AegisMavenModel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
